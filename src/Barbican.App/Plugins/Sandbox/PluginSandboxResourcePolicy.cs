// Mirrors: Bulwark proxy.ts:365-480 and host-bridge.ts:356-370; native resource and navigation enforcement supplement CSP.
using Barbican.Core.Common;
using Barbican.Core.Plugins.Permissions;
namespace Barbican.App.Plugins.Sandbox;
/// <summary>Production isolated origin policy; never routes mail, arbitrary filesystem paths or network.</summary>
public sealed class PluginSandboxResourcePolicy
{
    public string SandboxFlags { get { _ = this; throw new PortPendingException("M6b iframe sandbox flags"); } }
    /// <summary>The tier is selected by the host only after verifying its authorization.</summary>
    public string FlagsFor(PluginExecutionTier hostSelectedTier) { _ = this; throw new PortPendingException("M6b verified tier sandbox flags"); }
    public string ContentSecurityPolicy { get { _ = this; throw new PortPendingException("M6b sandbox CSP"); } }
    public bool Allows(Uri uri, string purpose) { _ = this; throw new PortPendingException("M6b sandbox resource policy"); }
}
