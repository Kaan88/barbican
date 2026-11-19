// Source: Bulwark stores/plugin-store.ts:188-320, 467-516; M6b adds exact review/generation and authenticated persistence boundaries.
using Barbican.Core.Common;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Runtime;
using Barbican.Core.Plugins.Persistence;

namespace Barbican.Core.Plugins.Packages;

/// <summary>The production package lifecycle and live authority source. Test-first stub; no executable loading.</summary>
public sealed class PluginPackageManager : IPluginPackageManager, IPluginAuthority
{
    public PluginPackageManager(PluginPackageManagerOptions options, IPluginPackageStore store, IPluginRevocationCoordinator revocation, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(revocation);
        ArgumentNullException.ThrowIfNull(time);
    }

    public event EventHandler<PluginAuthorityChangedEventArgs>? Changed { add { } remove { } }
    public Task<Result<PluginInstallPreview>> InspectAsync(Stream archive, PluginPackageSource source, CancellationToken ct) => throw new PortPendingException("M6b package lifecycle");
    public Task<Result<PluginInstallPreview>> ReviewAsync(PluginPackageIdentity installed, CancellationToken ct) => throw new PortPendingException("M6b package lifecycle");
    public Task<Result<PluginInstallation>> InstallAsync(PluginInstallDecision decision, CancellationToken ct) => throw new PortPendingException("M6b package lifecycle");
    public Task<Result<IReadOnlyList<PluginInstallation>>> ListAsync(CancellationToken ct) => throw new PortPendingException("M6b package lifecycle");
    public Task<Result<PluginInstallation?>> FindAsync(string pluginId, CancellationToken ct) => throw new PortPendingException("M6b package lifecycle");
    public Task<Result<Unit>> SetEnabledAsync(PluginPackageIdentity package, bool enabled, CancellationToken ct) => throw new PortPendingException("M6b package lifecycle");
    public Task<Result<Unit>> UninstallAsync(PluginPackageIdentity package, CancellationToken ct) => throw new PortPendingException("M6b package lifecycle");
    public Task<Result<Stream>> OpenResourceAsync(PluginPackageIdentity package, string resource, CancellationToken ct) => throw new PortPendingException("M6b package lifecycle");
    public Task<Result<PluginGrantSnapshot?>> GetCurrentAsync(string pluginId, CancellationToken ct) => throw new PortPendingException("M6b package lifecycle");
}
