// Mirrors: Bulwark host-api.ts and per-feature host-api-* adapters. Each method supplies executable input and output projection, never raw JMAP forwarding.
using Barbican.Core.Plugins.Protocol;
namespace Barbican.Core.Plugins.Bridge;
public interface IPluginApiMethod
{
    PluginApiDescriptor Descriptor { get; }
    PluginErrorCode? ValidateArguments(PluginValue arguments);
    Task<PluginCallOutcome> InvokeAsync(IPluginInvocationContext context, PluginValue arguments, CancellationToken ct);
}
