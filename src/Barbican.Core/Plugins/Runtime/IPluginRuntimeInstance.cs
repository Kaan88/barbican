// Mirrors: Bulwark protocol.ts hook/callback/shouldShow and host-change messages. Payloads must pass the reference codec and schemas.
using System.Text.Json;
using Barbican.Core.Common;
namespace Barbican.Core.Plugins.Runtime;
public interface IPluginRuntimeInstance : IAsyncDisposable
{
    PluginRuntimeBinding Binding { get; }
    PluginActivation Activation { get; }
    Task<Result<JsonElement>> InvokeHookAsync(string hookName, JsonElement arguments, TimeSpan budget, CancellationToken ct);
    Task<Result<JsonElement>> InvokeCallbackAsync(string callbackId, JsonElement arguments, TimeSpan budget, CancellationToken ct);
    Task<Result<bool>> ShouldShowAsync(string slot, JsonElement context, CancellationToken ct);
    Task<Result<Unit>> SendAsync(JsonElement hostMessage, CancellationToken ct);
}
