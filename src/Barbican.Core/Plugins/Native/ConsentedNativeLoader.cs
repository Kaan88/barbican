// Host-added Barbican native full-trust activation; Rust plugins.rs:64-107 and M6b consent precede executable loading.
using Barbican.Core.Common;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Runtime;
namespace Barbican.Core.Plugins.Native;

/// <summary>Loads exactly the consented native package under a live host-created binding.</summary>
public sealed class ConsentedNativeLoader
{
    public ConsentedNativeLoader(IPluginPackageManager packages, IPluginAuthority authority, IPluginRevocationCoordinator revocation)
    {
        ArgumentNullException.ThrowIfNull(packages);
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(revocation);
    }
#pragma warning disable CA1822 // Test-first instance seam; the implementation will use injected authority and packages.
    public Task<Result<NativePluginActivation>> LoadAsync(PluginRuntimeBinding binding, CancellationToken ct) => throw new PortPendingException("M6b native consented activation");
#pragma warning restore CA1822
}
