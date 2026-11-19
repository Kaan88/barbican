// Mirrors: Bulwark lib/plugin-sandbox/host-bridge.ts:318-322,521-541,614-666,718-726,740-763.
using Barbican.Core.Common;
using Barbican.Core.Plugins.Runtime;
namespace Barbican.App.Plugins.Sandbox;
/// <summary>Production per-frame reusable prop capabilities, one-shot replies and multi-chunk streams.</summary>
public sealed class PluginSandboxOwnedCalls(PluginRuntimeBinding owner, int maxCallbacks, int maxPending, int maxStreams) : IDisposable
{
    public PluginRuntimeBinding Owner { get; } = owner;
    public int MaxCallbacks { get; } = maxCallbacks;
    public int MaxPending { get; } = maxPending;
    public int MaxStreams { get; } = maxStreams;
    public bool ReplacePropCallbacks(IReadOnlyList<string> ids) { _ = Owner; throw new PortPendingException("M6b prop callback replacement"); }
    public bool CanInvokePropCallback(PluginRuntimeBinding claimant, string id) { _ = Owner; throw new PortPendingException("M6b reusable prop callback"); }
    public bool AddPendingReply(string id) { _ = Owner; throw new PortPendingException("M6b pending reply ownership"); }
    public bool TakePendingReply(PluginRuntimeBinding claimant, string id) { _ = Owner; throw new PortPendingException("M6b pending reply ownership"); }
    public bool BeginStream(string id) { _ = Owner; throw new PortPendingException("M6b stream ownership"); }
    public bool AcceptStreamChunk(PluginRuntimeBinding claimant, string id) { _ = Owner; throw new PortPendingException("M6b stream chunk ownership"); }
    public bool EndStream(PluginRuntimeBinding claimant, string id) { _ = Owner; throw new PortPendingException("M6b stream completion"); }
    public void Dispose() => throw new PortPendingException("M6b callback and stream teardown");
}
