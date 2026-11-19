// Mirrors: Bulwark host-bridge.ts instance construction; the App owns WebView/profile/frame authentication and initialization.
using Barbican.Core.Common;
namespace Barbican.Core.Plugins.Runtime;
public interface IPluginRuntimeFactory
{
    Task<Result<IPluginRuntimeInstance>> StartAsync(PluginRuntimeStart request, CancellationToken ct);
}
