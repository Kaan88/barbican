// Mirrors: Bulwark lib/plugin-sandbox/host-bridge.ts:427-477,740-771; native route events revoke before message acceptance.
using Barbican.Core.Common;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Runtime;
using Barbican.Core.Plugins.Protocol;
namespace Barbican.App.Plugins.Sandbox;
/// <summary>Production native ingress and lifetime gate; transport events are supplied only by the native adapter.</summary>
public sealed class PluginSandboxSession(PluginRuntimeBinding binding, IPluginSandboxWebView view, IPluginApiDispatcher dispatcher) : IAsyncDisposable
{
    public PluginRuntimeBinding Binding { get; } = binding;
    public IPluginSandboxWebView View { get; } = view;
    public IPluginApiDispatcher Dispatcher { get; } = dispatcher;
    /// <summary>Direct frame events must be rejected without invoking readPayload.</summary>
    public Task<bool> ReceiveAsync(object sourceView, bool topLevel, Uri source, Func<ReadOnlyMemory<byte>> readPayload, CancellationToken ct) { _ = Binding; throw new PortPendingException("M6b runtime native ingress"); }
    /// <summary>Issues the validated host init after the authenticated ready; API calls may arrive during activation.</summary>
    public Task<bool> IssueInitAsync(PluginValue message, CancellationToken ct) { _ = Binding; throw new PortPendingException("M6b runtime init"); }
    /// <summary>Invalidates the binding synchronously before asynchronous cleanup starts.</summary>
    public ValueTask NavigationStartingAsync(object sourceView, CancellationToken ct) { _ = Binding; throw new PortPendingException("M6b runtime navigation barrier"); }
    public ValueTask DisposeAsync() => throw new PortPendingException("M6b runtime teardown");
}
