// Mirrors: Bulwark host-api.ts and per-feature host-api-* adapters. Each method supplies executable input and output projection, never raw JMAP forwarding.
using System.Text.Json;
namespace Barbican.Core.Plugins.Bridge;
public interface IPluginApiMethod
{
    PluginApiDescriptor Descriptor { get; }
    PluginErrorCode? ValidateArguments(JsonElement arguments);
    Task<PluginCallOutcome> InvokeAsync(IPluginInvocationContext context, JsonElement arguments, CancellationToken ct);
}
