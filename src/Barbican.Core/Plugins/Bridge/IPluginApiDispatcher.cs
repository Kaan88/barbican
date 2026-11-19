// Mirrors: Bulwark host-api.ts dispatch. Binding is supplied only by authenticated native transport, never parsed from the request.
using Barbican.Core.Plugins.Runtime;
namespace Barbican.Core.Plugins.Bridge;
public interface IPluginApiDispatcher
{
    Task<PluginCallOutcome> DispatchAsync(PluginRuntimeBinding binding, PluginApiRequest request, CancellationToken ct);
}
