// Mirrors: Bulwark lib/plugin-sandbox/loader.ts and host-bridge.ts:327-459; authority must be re-read and revocation tracked before plugin code runs.
using Barbican.Core.Common;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Runtime;
namespace Barbican.App.Plugins.Sandbox;
/// <summary>Production factory contract; implementation is intentionally pending until red acceptance tests are reviewed.</summary>
public sealed class PluginSandboxFactory(IPluginPackageManager packages, IPluginAuthority authority, IPluginRevocationCoordinator revocation, IPluginApiDispatcher dispatcher, IPluginSandboxWebViewFactory views) : IPluginRuntimeFactory
{
    private readonly IPluginPackageManager _packages = packages;
    private readonly IPluginAuthority _authority = authority;
    private readonly IPluginRevocationCoordinator _revocation = revocation;
    private readonly IPluginApiDispatcher _dispatcher = dispatcher;
    private readonly IPluginSandboxWebViewFactory _views = views;
    public Task<Result<IPluginRuntimeInstance>> StartAsync(PluginRuntimeStart request, CancellationToken ct) => throw new PortPendingException("M6b production WebView runtime");
}
