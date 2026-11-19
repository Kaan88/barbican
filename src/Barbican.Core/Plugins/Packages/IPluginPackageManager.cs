// Mirrors: Bulwark stores/plugin-store.ts and extension-package.ts; inert review precedes executable loading.
using Barbican.Core.Common;
namespace Barbican.Core.Plugins.Packages;
public interface IPluginPackageManager
{
    Task<Result<PluginInstallPreview>> InspectAsync(Stream archive, PluginPackageSource source, CancellationToken ct);
    Task<Result<PluginInstallPreview>> ReviewAsync(PluginPackageIdentity installed, CancellationToken ct);
    Task<Result<PluginInstallation>> InstallAsync(PluginInstallDecision decision, CancellationToken ct);
    Task<Result<IReadOnlyList<PluginInstallation>>> ListAsync(CancellationToken ct);
    Task<Result<PluginInstallation?>> FindAsync(string pluginId, CancellationToken ct);
    Task<Result<Unit>> SetEnabledAsync(PluginPackageIdentity package, bool enabled, CancellationToken ct);
    Task<Result<Unit>> UninstallAsync(PluginPackageIdentity package, CancellationToken ct);
    /// <summary>Reads only a validated relative resource belonging to the exact installed content identity; caller owns the stream.</summary>
    Task<Result<Stream>> OpenResourceAsync(PluginPackageIdentity package, string resource, CancellationToken ct);
}
