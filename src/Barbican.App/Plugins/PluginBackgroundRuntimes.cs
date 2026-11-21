// Mirrors: Bulwark lib/plugin-sandbox/loader.ts (background instances for installed, enabled add-ons).
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;
using Barbican.Core.Settings.Pages;
using Barbican.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Barbican.App.Plugins;

/// <summary>Reconciles one background runtime per supported, enabled add-on.</summary>
internal sealed partial class PluginBackgroundRuntimes(
    PluginPackageHost packages, IProfilePaths paths, IPrefsStore prefs, ILogger<PluginBackgroundRuntimes> log, Barbican.Core.Plugins.Pages.PluginAddOnNames? names = null) : IAsyncDisposable
{
    private readonly SemaphoreSlim _changes = new(1, 1);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _starts = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ActiveAddOn> _active = new(StringComparer.Ordinal);
    private readonly Dictionary<PluginPackageIdentity, CancellationTokenSource> _starting = [];
    private readonly Lock _lifecycle = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _stopping;
    private Func<CancellationToken, Task<Result<IPluginRuntimeFactory>>>? _create;
    private IPluginRuntimeFactory? _factory;
    private bool _javaScriptAvailable = true;
    private int _operations;

    internal event EventHandler? RunningChanged;

    internal Task WaitUntilInitializedAsync(CancellationToken ct) => _initialized.Task.WaitAsync(ct);

    internal IReadOnlyList<IPluginRuntimeInstance> HookOwners(string hook) =>
        [.. _active.Values.Select(active => active.Instance)
            .Where(runtime => runtime.Binding.IsAlive && runtime.Activation.Hooks.Contains(hook, StringComparer.Ordinal))];

    internal IPluginRuntimeInstance? ActiveRuntime(PluginRuntimeBinding binding, string accountKey, string permission) =>
        binding.IsAlive && _active.TryGetValue(binding.Package.Id, out var active) && ReferenceEquals(active.Instance.Binding, binding) &&
        active.Grant.Authorizes(binding.Package, binding.GrantGeneration, permission, accountKey) ? active.Instance : null;

    internal bool IsRunning(PluginInstallation installation) =>
        installation is { Enabled: true, Error: null, Grant: { Enabled: true } grant } &&
        _active.TryGetValue(grant.Package.Id, out var active) && Matches(active.Instance.Binding, grant);

    internal async Task InitializeAsync(
        Func<CancellationToken, Task<Result<IPluginRuntimeFactory>>> create, CancellationToken ct, bool javaScriptAvailable = true)
    {
        if (Interlocked.CompareExchange(ref _create, create, null) is not null) return;
        Volatile.Write(ref _javaScriptAvailable, javaScriptAvailable);
        var operation = Begin(ct);
        if (operation is null)
        {
            _initialized.TrySetResult();
            return;
        }
        var token = operation.Token;
        try
        {
            if (await GetFactoryAsync(token).ConfigureAwait(false) is not { } factory)
            {
                LogRestoreIncomplete(log);
                return;
            }

            var installations = await packages.ListAsync(token).ConfigureAwait(false);
            if (!installations.IsOk || installations.Value is null)
            {
                LogRestoreIncomplete(log);
                return;
            }
            foreach (var installation in installations.Value.Where(CanStart).OrderBy(item => item.Package.Identity.Id, StringComparer.Ordinal))
            {
                if (!await StartAsync(factory, installation, token).ConfigureAwait(false)) LogRestoreIncomplete(log);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
#pragma warning disable CA1031 // A failed profile restore must not take down the mail window or expose plugin content.
        catch (Exception) { LogRestoreIncomplete(log); }
#pragma warning restore CA1031
        finally
        {
            _initialized.TrySetResult();
            End(operation);
        }
    }

    internal async Task<Result<Unit>> SetEnabledAsync(PluginPackageIdentity package, bool enabled, CancellationToken ct)
    {
        if (!enabled) return await ChangeInstallationAsync(package, remove: false, ct).ConfigureAwait(false);
        var operation = Begin(ct);
        if (operation is null) return Failed("The add-on host is closing.");
        var token = operation.Token;
        try
        {
            RunningChanged?.Invoke(this, EventArgs.Empty);
            if (await GetFactoryAsync(token).ConfigureAwait(false) is not { } factory) return Failed("Add-on startup is unavailable.");
            var current = await packages.FindAsync(package.Id, token).ConfigureAwait(false);
            if (current.IsOk && current.Value is { } item && item.Package.Identity == package &&
                item.Package.Source == PluginPackageSource.Managed)
                return await StartManagedAsync(factory, item, token).ConfigureAwait(false);
            var enabledItem = await CommitEnablementAsync(package, token).ConfigureAwait(false);
            if (!enabledItem.IsOk) return Result<Unit>.Fail(enabledItem.Error!);
            return await StartAsync(factory, enabledItem.Value!, token).ConfigureAwait(false)
                ? Result<Unit>.Ok(default)
                : Failed("The add-on is enabled but could not start.");
        }
        finally { End(operation); }
    }

    private async Task<Result<Unit>> StartManagedAsync(IPluginRuntimeFactory factory, PluginInstallation item, CancellationToken ct)
    {
        if (!IsSupported(item)) return Failed("Managed add-on startup is unavailable.");
        if (IsRunning(item)) return Result<Unit>.Ok(default);
        if (item.Enabled) return Failed("The managed add-on is already starting or needs to be disabled before retrying.");
        var enabled = await packages.EnableManagedAsync(item.Package.Identity, ct).ConfigureAwait(false);
        if (!enabled.IsOk) return Result<Unit>.Fail(enabled.Error!);
        var started = false;
        try
        {
            started = await StartAsync(factory, enabled.Value!, ct).ConfigureAwait(false);
            return started ? Result<Unit>.Ok(default) : Failed("Could not start the managed add-on.");
        }
        finally
        {
            if (!started) _ = await packages.RollBackManagedAsync(enabled.Value!.Grant!).ConfigureAwait(false);
        }
    }

    private async Task<IPluginRuntimeFactory?> GetFactoryAsync(CancellationToken ct)
    {
        if (Volatile.Read(ref _factory) is { } existing) return existing;
        await _changes.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_factory is not null) return _factory;
            var create = Volatile.Read(ref _create);
            if (create is null) return null;
            var result = await create(ct).ConfigureAwait(false);
            if (result.IsOk) Volatile.Write(ref _factory, result.Value);
            return _factory;
        }
        finally { _changes.Release(); }
    }

    private async Task<Result<PluginInstallation>> CommitEnablementAsync(PluginPackageIdentity package, CancellationToken ct)
    {
        await _changes.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var current = await packages.FindAsync(package.Id, ct).ConfigureAwait(false);
            if (!current.IsOk || current.Value is not { } before || before.Package.Identity != package || !IsSupported(before))
                return Result<PluginInstallation>.Fail(new ValidationError("The installed add-on is unavailable or has changed."));
            var changed = await packages.SetEnabledAsync(package, true, ct).ConfigureAwait(false);
            if (!changed.IsOk) return Result<PluginInstallation>.Fail(changed.Error!);

            current = await packages.FindAsync(package.Id, ct).ConfigureAwait(false);
            return current.IsOk && current.Value is { } after && after.Package.Identity == package && CanStart(after)
                ? Result<PluginInstallation>.Ok(after)
                : Result<PluginInstallation>.Fail(new ValidationError("Could not read the enabled add-on."));
        }
        finally { _changes.Release(); }
    }

    internal Task<Result<Unit>> UninstallAsync(PluginPackageIdentity package, CancellationToken ct) =>
        ChangeInstallationAsync(package, remove: true, ct);

    private async Task<Result<Unit>> ChangeInstallationAsync(PluginPackageIdentity package, bool remove, CancellationToken ct)
    {
        var operation = Begin(ct);
        if (operation is null) return Failed("The add-on host is closing.");
        var token = operation.Token;
        try
        {
            await _changes.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (_starting.TryGetValue(package, out var starting)) await starting.CancelAsync().ConfigureAwait(false);
                var result = remove
                    ? await packages.UninstallAsync(package, token).ConfigureAwait(false)
                    : await packages.SetEnabledAsync(package, false, token).ConfigureAwait(false);
                if (result.IsOk && _active.TryGetValue(package.Id, out var previous) &&
                    previous.Instance.Binding.Package == package && !previous.Instance.Binding.IsAlive)
                    _active.TryRemove(KeyValuePair.Create(package.Id, previous));
                return result;
            }
            finally { _changes.Release(); }
        }
        finally { End(operation); }
    }

    private async Task<bool> StartAsync(IPluginRuntimeFactory factory, PluginInstallation installation, CancellationToken ct)
    {
        var grant = installation.Grant!;
        names?.Remember(grant.Package.Id, installation.Package.Manifest);
        var start = _starts.GetOrAdd(grant.Package.Id, _ => new SemaphoreSlim(1, 1));
        await start.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (!await AdmitStartAsync(grant, budget, ct).ConfigureAwait(false)) return false;
            try
            {
                if (_active.TryGetValue(grant.Package.Id, out var previous))
                {
                    if (Matches(previous.Instance.Binding, grant)) return !budget.IsCancellationRequested;
                    await previous.Instance.DisposeAsync().ConfigureAwait(false);
                    _active.TryRemove(KeyValuePair.Create(grant.Package.Id, previous));
                }
                budget.Token.ThrowIfCancellationRequested();
                budget.CancelAfter(TimeSpan.FromSeconds(25));
                var result = await factory.StartAsync(Request(installation), budget.Token).ConfigureAwait(false);
                if (!result.IsOk || result.Value is not { } instance) return false;
                if (budget.IsCancellationRequested || !Matches(instance.Binding, grant))
                {
                    await instance.DisposeAsync().ConfigureAwait(false);
                    return false;
                }

                _active[grant.Package.Id] = new(instance, grant);
                _ = instance.Binding.Lifetime.Register(() => RunningChanged?.Invoke(this, EventArgs.Empty));
                RunningChanged?.Invoke(this, EventArgs.Empty);
                return instance.Binding.IsAlive;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && budget.IsCancellationRequested) { return false; }
            finally
            {
                await _changes.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                try { _starting.Remove(grant.Package); }
                finally { _changes.Release(); }
            }
        }
        finally { start.Release(); }
    }

    private async Task<bool> AdmitStartAsync(PluginGrantSnapshot grant, CancellationTokenSource budget, CancellationToken ct)
    {
        await _changes.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var current = await packages.FindAsync(grant.Package.Id, ct).ConfigureAwait(false);
            if (!current.IsOk || current.Value is not { } item || !CanStart(item) ||
                item.Grant!.Package != grant.Package || item.Grant.Generation != grant.Generation) return false;
            _starting.Add(grant.Package, budget);
            return true;
        }
        finally { _changes.Release(); }
    }

    private bool IsSupported(PluginInstallation installation) =>
        installation.Error is null && installation.Grant?.Tier == installation.Package.RequestedTier &&
        (installation.Package.RuntimeKind, installation.Package.RequestedTier) switch
        {
            (PluginRuntimeKind.JavaScript, PluginExecutionTier.Untrusted) => Volatile.Read(ref _javaScriptAvailable),
            (PluginRuntimeKind.JavaScript, PluginExecutionTier.Privileged) =>
                OperatingSystem.IsWindows() && Volatile.Read(ref _javaScriptAvailable) && installation.Package.Source == PluginPackageSource.Managed,
            (PluginRuntimeKind.Native, PluginExecutionTier.NativeFullTrust) => OperatingSystem.IsWindows(),
            _ => false,
        };

    private bool CanStart(PluginInstallation installation) =>
        IsSupported(installation) && installation is { Enabled: true, Grant.Enabled: true };

    private static bool Matches(PluginRuntimeBinding binding, PluginGrantSnapshot grant) =>
        binding.IsAlive && binding.Package == grant.Package && binding.GrantGeneration == grant.Generation &&
        binding.Tier == grant.Tier && binding.Kind == PluginFrameKind.Background;

    private PluginRuntimeStart Request(PluginInstallation installation)
    {
        var value = new PluginValueBuilder();
        return new PluginRuntimeStart(installation, installation.Grant!, paths.Profile, PluginFrameKind.Background, null,
            value.Build(value.Object()), HostState(null));
    }

    /// <summary>The slot-frame start of <paramref name="slot"/> for an installed add-on, with the host's current settings and theme (Bulwark runtime.tsx:1495-1603).</summary>
    internal PluginRuntimeStart SlotRequest(PluginInstallation installation, string slot, PluginValue props, bool fill, JsonElement theme,
        Barbican.Core.Plugins.Bridge.PluginHostCallbacks? callbacks = null) =>
        new(installation, installation.Grant!, paths.Profile, PluginFrameKind.Slot, slot, props, HostState(theme), fill, callbacks);

    /// <summary>The offers the add-on's running background instance made at activation, or null while it is not running.</summary>
    internal PluginActivation? ActivationOf(string packageId) =>
        _active.TryGetValue(packageId, out var active) && active.Instance.Binding.IsAlive ? active.Instance.Activation : null;

    private PluginHostState HostState(JsonElement? theme)
    {
        var settings = prefs.Current;
        var culture = CultureInfo.CurrentCulture;
        var locale = string.IsNullOrWhiteSpace(culture.Name) ? "en" : culture.Name;
        var timeZone = SettingDisplayTimeZone.EventZone(settings.Str("timeZone", "auto"), TimeZoneInfo.Local);
        var weekStart = settings.Number("firstDayOfWeek", (int)culture.DateTimeFormat.FirstDayOfWeek);
        var regional = JsonSerializer.SerializeToElement(new
        {
            locale,
            dateLocale = locale,
            hour12 = string.Equals(settings.Str("timeFormat", "24h"), "12h", StringComparison.Ordinal),
            timeZone = timeZone.Length == 0 ? "UTC" : timeZone,
            firstDayOfWeek = weekStart is 0 or 1 or 6 ? (int)weekStart : 1,
        });
        using var empty = JsonDocument.Parse("{}");
        var features = JsonSerializer.SerializeToElement(new
        {
            externalSenderLabel = false,
            externalRecipientWarning = false,
            noThirdPartyTraffic = PluginNetworkPolicy.IsBlocked(prefs),
        });
        return new PluginHostState(locale, regional, theme ?? empty.RootElement, empty.RootElement, features);
    }

    private static Result<Unit> Failed(string message) => Result<Unit>.Fail(new ValidationError(message));

    private sealed record ActiveAddOn(IPluginRuntimeInstance Instance, PluginGrantSnapshot Grant);

    private CancellationTokenSource? Begin(CancellationToken ct)
    {
        lock (_lifecycle)
        {
            if (_stopping is not null) return null;
            var operation = CancellationTokenSource.CreateLinkedTokenSource(ct, _stop.Token);
            _operations++;
            return operation;
        }
    }

    private void End(CancellationTokenSource operation)
    {
        operation.Dispose();
        lock (_lifecycle)
        {
            if (--_operations == 0 && _stopping is not null) _drained.TrySetResult();
        }
    }

    private Task DrainAsync()
    {
        lock (_lifecycle)
        {
            if (_operations == 0) _drained.TrySetResult();
            return _stopping ??= Task.WhenAll(_stop.CancelAsync(), _drained.Task);
        }
    }

    internal async Task<bool> StopAsync()
    {
        try
        {
            await DrainAsync().WaitAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException) { return false; }
    }

    // The package host and its revocation coordinator own teardown of active instances.
    public async ValueTask DisposeAsync()
    {
        await DrainAsync().ConfigureAwait(false);
        _stop.Dispose();
        _changes.Dispose();
        foreach (var start in _starts.Values) start.Dispose();
    }

    [LoggerMessage(EventId = 812, Level = LogLevel.Warning, Message = "Could not restore one or more enabled add-ons.")]
    private static partial void LogRestoreIncomplete(ILogger logger);
}
