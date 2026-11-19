// Mirrors: Bulwark lib/plugin-sandbox/host-bridge.ts:318-322,614-666,740-771; callback and stream ownership is per frame instance.
using Barbican.Core.Common;
using Barbican.Core.Plugins.Runtime;
namespace Barbican.App.Plugins.Sandbox;
/// <summary>Production bounded per-instance callback and stream registry.</summary>
public sealed class PluginSandboxOwnedCalls(PluginRuntimeBinding owner, int maxCallbacks, int maxStreams) : IDisposable
{
    public PluginRuntimeBinding Owner { get; } = owner;
    public int MaxCallbacks { get; } = maxCallbacks;
    public int MaxStreams { get; } = maxStreams;
    public bool AddCallback(string id) { _ = Owner; throw new PortPendingException("M6b callback ownership"); }
    public bool TakeCallback(PluginRuntimeBinding claimant, string id) { _ = Owner; throw new PortPendingException("M6b callback ownership"); }
    public bool AddStream(string id) { _ = Owner; throw new PortPendingException("M6b stream ownership"); }
    public bool TakeStream(PluginRuntimeBinding claimant, string id) { _ = Owner; throw new PortPendingException("M6b stream ownership"); }
    public void Dispose() => throw new PortPendingException("M6b callback and stream teardown");
}
