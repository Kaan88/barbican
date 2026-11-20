// M6b: own the authenticated package store and profile lease together.
using Barbican.Core.Accounts;
using Barbican.Core.Auth;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Net;
using Barbican.App.Plugins.Sandbox;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Native;
using Barbican.Core.Plugins.OAuth;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Persistence;
using Barbican.Core.Plugins.Runtime;
using Barbican.Core.Storage;
using Barbican.Platform;
using Barbican.Platform.Plugins;

namespace Barbican.App.Plugins;

internal sealed class PluginPackageHost(IProfilePaths paths, ISecretStore secrets, TimeProvider time, IAccountOverview accounts) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private PluginPackageManager? _manager;
    private PluginRevocationCoordinator? _revocation;
    private PluginOAuthCallbacks? _oauth;
    private PluginOAuthRedirect? _oauthRedirect;
    private Task? _oauthStop;
    private GuardedHttp? _network;
    private PluginHttpFetch? _httpFetch;
    private AuthenticatedPluginPackageStore? _store;
    private IPluginProfileAuthorityLease? _lease;
    private DirectoryInfo? _automationRoot;
    private Task<Result<Unit>>? _shutdown;
    private Task<Result<Unit>>? _runtimeShutdown;
    private bool _closed;
    private bool _cleanupNeedsRetry;

#if AUTOMATION
    internal bool UseSyntheticHttpProbe { private get; init; }

    internal Task<Result<string>> ReadSampleSendFixtureAsync(string fixtureId, CancellationToken ct) =>
        WithManagerAsync(async _ =>
        {
            if (fixtureId is not ("it-plugin-api-probe" or "sample-send-observer" or "sample-send-policy") ||
                !ProfilePaths.IsAutomationProfile(paths.Profile) ||
                !SampleEnabled("BARBICAN_SAMPLE") || !SampleEnabled("BARBICAN_SAMPLE_ADDON_ACCOUNT") ||
                !SampleEnabled("BARBICAN_SAMPLE_SEND_PIPELINE"))
                return Result<string>.Fail(new ValidationError("Sample send records are unavailable."));
            var snapshot = await _store!.LoadAsync(paths.Profile, fixtureId, "sample", ct).ConfigureAwait(false);
            if (!snapshot.IsOk || snapshot.Value!.Index.IsEmpty)
                return Result<string>.Fail(new ValidationError("The sample fixture has no send records."));
            using var document = System.Text.Json.JsonDocument.Parse(snapshot.Value.Index.AsMemory());
            var records = new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.Ordinal);
            foreach (var pair in document.RootElement.EnumerateArray())
            {
                if (pair[0].GetString() is "actions" or "sendChecks" or "transforms" or "afterSend" &&
                    pair[1].ValueKind == System.Text.Json.JsonValueKind.Array)
                    records[pair[0].GetString()!] = pair[1];
            }
            return Result<string>.Ok(System.Text.Json.JsonSerializer.Serialize(records));
        }, ct);

    private static bool SampleEnabled(string name) =>
        string.Equals(Environment.GetEnvironmentVariable(name), "1", StringComparison.Ordinal);
#endif

    public Task<Result<IReadOnlyList<PluginInstallation>>> ListAsync(CancellationToken ct) =>
        WithManagerAsync(manager => manager.ListAsync(ct), ct);

    internal Task<Result<PluginInstallation?>> FindAsync(string id, CancellationToken ct) =>
        WithManagerAsync(manager => manager.FindAsync(id, ct), ct);

    internal Task<bool> TryPostAsync(PluginRuntimeBinding binding, Func<PluginGrantSnapshot, bool> write, CancellationToken ct) =>
        Volatile.Read(ref _manager) is { } manager ? manager.TryPostAsync(binding, write, ct) : Task.FromResult(false);

    internal Task<bool> TryPostWithContinuationAsync(PluginRuntimeBinding binding,
        Func<PluginGrantSnapshot, Func<Func<bool>, bool>, bool> write, CancellationToken ct) =>
        Volatile.Read(ref _manager) is { } manager
            ? manager.TryPostAsync(binding, grant => write(grant, use => ReferenceEquals(Volatile.Read(ref _manager), manager) &&
                manager.TryContinuePost(binding, use)), ct)
            : Task.FromResult(false);

    internal Task<bool> TryRunWithGrantsAsync(IReadOnlyCollection<PluginRuntimeBinding> bindings, Func<bool> issue, CancellationToken ct) =>
        Volatile.Read(ref _manager) is { } manager ? manager.TryRunWithGrantsAsync(bindings, issue, ct) : Task.FromResult(false);

    public IReadOnlyList<AccountStatus> AccountStatuses => accounts.Statuses;

    public IReadOnlyList<AccountInfo> EligibleAccounts => [.. accounts.Statuses
        .Where(status => status.IsConnected && !status.NeedsSignIn).Select(status => status.Account)];

    public Task<Result<PluginInstallPreview>> InspectAsync(Stream archive, CancellationToken ct) =>
        WithManagerAsync(manager => manager.InspectAsync(archive, PluginPackageSource.LocalImport, ct), ct);

    internal Task<Result<PluginInstallPreview>> ReviewAsync(PluginPackageIdentity package, CancellationToken ct) =>
        WithManagerAsync(manager => manager.ReviewAsync(package, ct), ct);

    public Task<Result<PluginInstallation>> InstallAsync(PluginInstallDecision decision, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(decision);
        return WithManagerAsync(manager =>
        {
            var availableAccounts = EligibleAccounts.Select(account => account.Key).ToHashSet(StringComparer.Ordinal);
            if (!decision.AccountKeys.IsSubsetOf(availableAccounts))
            {
                return Task.FromResult(Result<PluginInstallation>.Fail(
                    new ValidationError("The selected mail accounts changed. Review the add-on again.")));
            }
            return manager.InstallAsync(decision, ct);
        }, ct);
    }

    internal Task<Result<Unit>> SetEnabledAsync(PluginPackageIdentity package, bool enabled, CancellationToken ct) =>
        WithManagerAsync(manager => manager.SetEnabledAsync(package, enabled, ct), ct);

    internal async Task<bool> TryHoldStorageAuthorityAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_closed) return false;
            var acquired = await GetLeaseAsync(await PackagePathsAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false);
            if (!acquired.IsOk || acquired.Value is not { IsHeld: true } lease) return false;
            _lease = lease;
            return !lease.Lifetime.IsCancellationRequested;
        }
        finally { _gate.Release(); }
    }

    internal async Task<Result<Unit>> PrepareSignOutAsync(IReadOnlyList<string> localAccountKeys, bool allAccounts, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var failure = Result<Unit>.Fail(new ValidationError("Could not erase add-on data. Try signing out again."));
            if (_closed) return failure;
            var packagePaths = await PackagePathsAsync(ct).ConfigureAwait(false);
            var acquired = await GetLeaseAsync(packagePaths, ct).ConfigureAwait(false);
            if (!acquired.IsOk) return failure;
            // Keep the profile fenced while the account manager detaches and forgets the login.
            _lease = acquired.Value!;
            var erased = await AuthenticatedPluginPackageStore.ClearSignedOutProfileAsync(packagePaths, _lease, secrets,
                localAccountKeys, allAccounts, ct).ConfigureAwait(false);
            return erased.IsOk ? erased : failure;
        }
        finally { _gate.Release(); }
    }

    internal Task<Result<Unit>> UninstallAsync(PluginPackageIdentity package, CancellationToken ct) =>
        WithManagerAsync(async manager =>
        {
            var installed = await manager.FindAsync(package.Id, ct).ConfigureAwait(false);
            if (!installed.IsOk) return Result<Unit>.Fail(installed.Error!);
            if (installed.Value?.Package.Identity == package)
            {
                var disabled = await manager.SetEnabledAsync(package, false, ct).ConfigureAwait(false);
                if (!disabled.IsOk) return disabled;
                // Keep the disabled package visible and retryable until its data has been erased.
                var cleared = await _store!.ClearUserStorageAsync(paths.Profile, package.Id, ct).ConfigureAwait(false);
                if (!cleared.IsOk) return cleared;
            }
            return await manager.UninstallAsync(package, ct).ConfigureAwait(false);
        }, ct);

    internal Task<Result<IPluginRuntimeFactory>> CreateSandboxFactoryAsync(
        IPluginSandboxWebViewFactory views, IPluginSandboxStartup startup, HookBus hooks,
        IPluginSessionSource sessions, IEnumerable<IPluginApiMethod> methods, CancellationToken ct, bool includeUserStorage = false) =>
        CreateAddOnFactoryAsync(views, startup, hooks, sessions, methods, includeUserStorage, nativePlugins: null, oauthBrowser: null,
            networkPrefs: null, ct);

    internal Task<Result<IPluginRuntimeFactory>> CreateAddOnFactoryAsync(
        IPluginSandboxWebViewFactory? views, IPluginSandboxStartup? startup, HookBus hooks,
        IPluginSessionSource sessions, IEnumerable<IPluginApiMethod> methods, bool includeUserStorage,
        NativePlugins? nativePlugins, PluginOAuthBrowser? oauthBrowser, IPrefsStore? networkPrefs, CancellationToken ct) =>
        WithManagerAsync(manager =>
        {
            // Lifecycle writes and runtime calls share the same leased authority and revocation coordinator.
            IPluginRuntimeFactory? sandbox = null;
            if (views is not null && startup is not null)
            {
                var adapters = includeUserStorage
                    ? methods.Concat(PluginStorageMethods.For(new PluginScopedStorage(paths.Profile, manager, sessions, _store!)))
                    : methods;
                _oauth ??= new PluginOAuthCallbacks(manager, time);
                _oauthRedirect ??= new PluginOAuthRedirect(_oauth, time);
                if (networkPrefs is not null)
                {
                    _httpFetch ??= CreateHttpAdapter(manager, networkPrefs);
                    adapters = adapters.Append(_httpFetch);
                }
                var dispatcher = new PluginApiDispatcher(paths.Profile,
                    adapters.Concat(PluginOAuthMethods.For(_oauth, _oauthRedirect, manager, oauthBrowser?.Launcher, oauthBrowser?.Confirm)),
                    manager, sessions, time);
                sandbox = new PluginSandboxFactory(manager, manager, _revocation!, dispatcher, views, startup, hooks, _oauth);
            }
            var native = nativePlugins?.CreatePackageFactory(manager, _revocation!);
            return Task.FromResult(Result<IPluginRuntimeFactory>.Ok(new PackageRuntimeFactory(sandbox, native)));
        }, ct);

    private PluginHttpFetch CreateHttpAdapter(PluginPackageManager manager, IPrefsStore prefs)
    {
#if AUTOMATION
        if (UseSyntheticHttpProbe) return PluginHttpFetch.ForSyntheticProbe(manager, prefs);
#endif
        _network ??= GuardedHttp.ForPluginFetch();
        return new PluginHttpFetch(manager, _network, prefs);
    }

    internal sealed record PluginOAuthBrowser(IAuthorizationLauncher Launcher, Func<PluginRuntimeBinding, Uri, CancellationToken, Task<bool>> Confirm);

    private sealed class PackageRuntimeFactory(IPluginRuntimeFactory? sandbox, IPluginRuntimeFactory? native) : IPluginRuntimeFactory
    {
        public Task<Result<IPluginRuntimeInstance>> StartAsync(PluginRuntimeStart request, CancellationToken ct)
        {
            var factory = request.Installation.Package.RuntimeKind switch
            {
                PluginRuntimeKind.JavaScript => sandbox,
                PluginRuntimeKind.Native => native,
                _ => null,
            };
            return factory?.StartAsync(request, ct) ??
                Task.FromResult(Result<IPluginRuntimeInstance>.Fail(new ValidationError("Add-on startup is unavailable.")));
        }
    }

    public async Task DiscardReviewAsync(string reviewId, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_closed && _manager is { } manager)
                await manager.DiscardReviewAsync(reviewId, ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private async Task<Result<T>> WithManagerAsync<T>(Func<PluginPackageManager, Task<Result<T>>> action, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_closed) return Result<T>.Fail(new ValidationError("The plugin package host is closed."));

            var opened = await OpenAsync(ct).ConfigureAwait(false);
            return opened.IsOk
                ? await action(opened.Value!).ConfigureAwait(false)
                : Result<T>.Fail(opened.Error!);
        }
        finally { _gate.Release(); }
    }

    private async Task<Result<PluginPackageManager>> OpenAsync(CancellationToken ct)
    {
        if (_manager is { } existing) return Result<PluginPackageManager>.Ok(existing);
        var packagePaths = await PackagePathsAsync(ct).ConfigureAwait(false);
        var acquired = await GetLeaseAsync(packagePaths, ct).ConfigureAwait(false);
        if (!acquired.IsOk) return Result<PluginPackageManager>.Fail(acquired.Error!);

        var lease = acquired.Value!;
        try
        {
            var opened = await AuthenticatedPluginPackageStore.OpenAsync(packagePaths, lease, secrets, ct).ConfigureAwait(false);
            if (!opened.IsOk) return Result<PluginPackageManager>.Fail(opened.Error!);

            _store = opened.Value!;
            _revocation = new PluginRevocationCoordinator(paths.Profile, time);
            _manager = new PluginPackageManager(new(paths.Profile, "1.21.2"), _store, _revocation, time, lease);
            _lease = lease;
            return Result<PluginPackageManager>.Ok(_manager);
        }
        finally
        {
            if (_lease is null)
            {
                _store?.Dispose();
                _store = null;
                await lease.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private Task<Result<IPluginProfileAuthorityLease>> GetLeaseAsync(IProfilePaths packagePaths, CancellationToken ct) =>
        _lease is { } held ? Task.FromResult(Result<IPluginProfileAuthorityLease>.Ok(held))
            : new PluginProfileAuthorityLeaseFactory().AcquireAsync(packagePaths, ct);

    private async Task<IProfilePaths> PackagePathsAsync(CancellationToken ct)
    {
        if (!ProfilePaths.IsAutomationProfile(paths.Profile)) return paths;

        // Automation secrets are process-local. Keep the matching index process-local too.
        _automationRoot ??= await Task.Run(() => Directory.CreateTempSubdirectory("barbican-packages-"), ct).ConfigureAwait(false);
        return ProfilePaths.At(paths.Profile, _automationRoot.FullName, _automationRoot.FullName);
    }

    public async ValueTask DisposeAsync()
    {
        var result = await ShutdownAsync().ConfigureAwait(false);
        if (!result.IsOk) throw new InvalidOperationException(result.Error!.Message);
    }

    internal async Task<Result<Unit>> ShutdownAsync()
    {
        Task<Result<Unit>> shutdown;
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            CloseAdmissionAndInvalidateBindings();
            if (_shutdown is { IsCompleted: true } && Volatile.Read(ref _cleanupNeedsRetry))
            {
                _shutdown = null;
                _runtimeShutdown = StopRuntimesAsync();
            }
            shutdown = _shutdown ??= CloseAsync();
        }
        finally { _gate.Release(); }
        return await shutdown.ConfigureAwait(false);
    }

    /// <summary>Refuses new package operations and invalidates tracked runtimes while retaining the lease.</summary>
    internal async Task BeginRuntimeShutdownAsync()
    {
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try { CloseAdmissionAndInvalidateBindings(); }
        finally { _gate.Release(); }
    }

    private void CloseAdmissionAndInvalidateBindings()
    {
        if (_runtimeShutdown is not null) return;
        _closed = true;
        _manager?.Dispose();
        _oauthStop = _oauthRedirect?.DisposeAsync().AsTask();
        _runtimeShutdown = StopRuntimesAsync();
    }

    private Task<Result<Unit>> StopRuntimesAsync() =>
        _revocation?.CloseAsync(CancellationToken.None) ?? Task.FromResult(Result<Unit>.Ok(default));

    private async Task<Result<Unit>> CloseAsync()
    {
#pragma warning disable VSTHRD003 // The shared revocation task runs bounded teardown with the UI dispatcher kept alive.
        var stopped = await _runtimeShutdown!.ConfigureAwait(false);
#pragma warning restore VSTHRD003
        Volatile.Write(ref _cleanupNeedsRetry, !stopped.IsOk);
        if (!stopped.IsOk) return stopped;
#pragma warning disable VSTHRD003 // The loopback listener's shutdown is independent of UI and package locks.
        if (_oauthStop is { } oauthStop) await oauthStop.ConfigureAwait(false);
#pragma warning restore VSTHRD003
        _network?.Dispose();
        _store?.Dispose();
        if (_lease is { } lease) await lease.DisposeAsync().ConfigureAwait(false);
        if (_automationRoot is { } root)
        {
            try { await Task.Run(() => root.Delete(recursive: true), CancellationToken.None).ConfigureAwait(false); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
        return stopped;
    }
}
