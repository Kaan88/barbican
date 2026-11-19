// Mirrors: Bulwark protocol.ts hook/callback/shouldShow and host-change messages. Payloads must pass the reference codec and schemas.
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Common;
namespace Barbican.Core.Plugins.Runtime;
public interface IPluginRuntimeInstance : IAsyncDisposable
{
    PluginRuntimeBinding Binding { get; }
    PluginActivation Activation { get; }
    Task<Result<PluginValue>> InvokeHookAsync(string hookName, PluginValue arguments, TimeSpan budget, CancellationToken ct);
    Task<Result<PluginValue>> InvokeCallbackAsync(string callbackId, PluginValue arguments, TimeSpan budget, CancellationToken ct);
    Task<Result<bool>> ShouldShowAsync(string slot, PluginValue context, CancellationToken ct);
    Task<Result<Unit>> SendAsync(PluginValue hostMessage, CancellationToken ct);
}
