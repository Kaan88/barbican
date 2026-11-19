// Mirrors: Bulwark host-bridge disposal and package disable/uninstall; stop old generations before deleting their resources.
using Barbican.Core.Common;
using Barbican.Core.Plugins.Packages;
namespace Barbican.Core.Plugins.Runtime;
public interface IPluginRevocationCoordinator
{
    /// <summary>Transfers cleanup ownership on success. On rejection, the caller must dispose its resource.</summary>
    /// <remarks>Disposing the registration detaches an independently cleaned resource; it does not perform cleanup.</remarks>
    Result<IDisposable> Track(PluginRuntimeBinding binding, Func<CancellationToken, ValueTask> teardown);
    Task<Result<Unit>> RevokeAsync(PluginPackageIdentity package, long throughGeneration, CancellationToken ct);
}
