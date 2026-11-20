// M6b: own the authenticated package store and profile lease together.
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Persistence;
using Barbican.Core.Plugins.Runtime;
using Barbican.Core.Storage;
using Barbican.Platform;
using Barbican.Platform.Plugins;

namespace Barbican.App.Plugins;

internal sealed class PluginPackageHost(IProfilePaths paths, ISecretStore secrets, TimeProvider time) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private PluginPackageManager? _manager;
    private AuthenticatedPluginPackageStore? _store;
    private IPluginProfileAuthorityLease? _lease;
    private DirectoryInfo? _automationRoot;
    private bool _closed;

    public async Task<Result<IReadOnlyList<PluginInstallation>>> ListAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_closed)
                return Result<IReadOnlyList<PluginInstallation>>.Fail(new ValidationError("The plugin package host is closed."));

            var opened = await OpenAsync(ct).ConfigureAwait(false);
            return opened.IsOk
                ? await opened.Value!.ListAsync(ct).ConfigureAwait(false)
                : Result<IReadOnlyList<PluginInstallation>>.Fail(opened.Error!);
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
            _manager = new PluginPackageManager(new(paths.Profile, "1.21.2"), _store, new PluginRevocationCoordinator(paths.Profile, time), time, lease);
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
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (_closed) return;
            _closed = true;
            _manager?.Dispose();
            _store?.Dispose();
            if (_lease is { } lease) await lease.DisposeAsync().ConfigureAwait(false);
            if (_automationRoot is { } root)
            {
                try { await Task.Run(() => root.Delete(recursive: true), CancellationToken.None).ConfigureAwait(false); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            }
        }
        finally { _gate.Release(); }
    }
}
