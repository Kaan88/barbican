// Mirrors: Bulwark host-bridge disposal and package disable/uninstall; stop old generations before deleting their resources.
using Barbican.Core.Common;
using Barbican.Core.Plugins.Packages;
namespace Barbican.Core.Plugins.Runtime;
public interface IPluginRevocationCoordinator
{
    Result<IDisposable> Track(PluginRuntimeBinding binding, Func<CancellationToken, ValueTask> teardown);
    Task<Result<Unit>> RevokeAsync(PluginPackageIdentity package, long throughGeneration, CancellationToken ct);
}
