// Mirrors: Bulwark lib/plugin-sandbox/host-bridge.ts:327-423; a dedicated profile and view are required for each instance.
namespace Barbican.App.Plugins.Sandbox;
public interface IPluginSandboxWebViewFactory
{
    Task<IPluginSandboxWebView> CreateAsync(string profile, CancellationToken ct);
}
