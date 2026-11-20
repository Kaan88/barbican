// Mirrors: Bulwark lib/plugin-sandbox/host-bridge.ts instance disposal.
using Barbican.Core.Common;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;
namespace Barbican.App.Plugins.Sandbox;
internal sealed class PluginSandboxStartupOwnedInstance(IPluginRuntimeInstance inner, PluginSandboxStartupLifetime lifetime) : IPluginRuntimeInstance
{
    public PluginRuntimeBinding Binding => inner.Binding;
    public PluginActivation Activation => inner.Activation;
    public Task<Result<PluginValue>> InvokeHookAsync(string hookName, PluginValue arguments, TimeSpan budget, CancellationToken ct) => inner.InvokeHookAsync(hookName, arguments, budget, ct);
    public Task<Result<PluginValue>> InvokeCallbackAsync(string callbackId, PluginValue arguments, TimeSpan budget, CancellationToken ct) => inner.InvokeCallbackAsync(callbackId, arguments, budget, ct);
    public Task<Result<bool>> ShouldShowAsync(string slot, PluginValue context, CancellationToken ct) => inner.ShouldShowAsync(slot, context, ct);
    public Task<Result<Unit>> SendAsync(PluginValue hostMessage, CancellationToken ct) => inner.SendAsync(hostMessage, ct);
    public ValueTask DisposeAsync() => lifetime.DisposeAsync();
}
