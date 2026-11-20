// M6b: own the authenticated package store and profile lease together.
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.App.Plugins.Sandbox;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Packages;
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
    private AuthenticatedPluginPackageStore? _store;
    private IPluginProfileAuthorityLease? _lease;
    private DirectoryInfo? _automationRoot;
    private Task<Result<Unit>>? _shutdown;
    private bool _closed;

    public Task<Result<IReadOnlyList<PluginInstallation>>> ListAsync(CancellationToken ct) =>
        WithManagerAsync(manager => manager.ListAsync(ct), ct);

    public IReadOnlyList<AccountStatus> AccountStatuses => accounts.Statuses;

    public IReadOnlyList<AccountInfo> EligibleAccounts => [.. accounts.Statuses
        .Where(status => status.IsConnected && !status.NeedsSignIn).Select(status => status.Account)];

    public Task<Result<PluginInstallPreview>> InspectAsync(Stream archive, CancellationToken ct) =>
        WithManagerAsync(manager => manager.InspectAsync(archive, PluginPackageSource.LocalImport, ct), ct);

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

    internal Task<Result<IPluginRuntimeFactory>> CreateSandboxFactoryAsync(
        IPluginSandboxWebViewFactory views, IPluginSandboxStartup startup, HookBus hooks,
        IPluginSessionSource sessions, IEnumerable<IPluginApiMethod> methods, CancellationToken ct) =>
        WithManagerAsync(manager =>
        {
            // Lifecycle writes and runtime calls share the same leased authority and revocation coordinator.
            var dispatcher = new PluginApiDispatcher(paths.Profile, methods, manager, sessions, time);
            var factory = new PluginSandboxFactory(manager, manager, _revocation!, dispatcher, views, startup, hooks);
            return Task.FromResult(Result<IPluginRuntimeFactory>.Ok(factory));
        }, ct);

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
        var acquired = await new PluginProfileAuthorityLeaseFactory().AcquireAsync(packagePaths, ct).ConfigureAwait(false);
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
            if (_shutdown is null)
            {
                _closed = true;
                _shutdown = CloseAsync();
            }
            shutdown = _shutdown;
        }
        finally { _gate.Release(); }
        return await shutdown.ConfigureAwait(false);
    }

    private async Task<Result<Unit>> CloseAsync()
    {
        _manager?.Dispose();
        var stopped = _revocation is { } revocation
            ? await revocation.CloseAsync(CancellationToken.None).ConfigureAwait(false)
            : Result<Unit>.Ok(default);
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
