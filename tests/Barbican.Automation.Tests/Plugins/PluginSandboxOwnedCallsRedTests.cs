// Source-derived ownership acceptance: Bulwark host-bridge.ts:318-322,521-541,614-666,718-726,740-763.
using Barbican.App.Plugins.Sandbox;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Runtime;
namespace Barbican.Automation.Tests.Plugins;
public sealed class PluginSandboxOwnedCallsRedTests
{
    private static PluginRuntimeBinding Binding() => PluginRuntimeBinding.Create("auto-8931", new PluginGrantSnapshot(new PluginPackageIdentity("fixture","1.0.0",new string('a',64)),1,true,[],[]), PluginFrameKind.Background);
    [Fact]
    public void Prop_callback_is_reusable_owned_and_revoked_on_replacement()
    {
        var owner=Binding(); var sibling=Binding(); var calls=new PluginSandboxOwnedCalls(owner,1,1,1);
        Assert.True(calls.TryReplacePropCallbacks(1,out var first));
        Assert.False(calls.CanInvokePropCallback(sibling,first[0]));
        Assert.True(calls.CanInvokePropCallback(owner,first[0])); Assert.True(calls.CanInvokePropCallback(owner,first[0]));
        Assert.False(calls.TryReplacePropCallbacks(2,out _)); Assert.True(calls.CanInvokePropCallback(owner,first[0]));
        Assert.True(calls.TryReplacePropCallbacks(1,out var next));
        Assert.False(calls.CanInvokePropCallback(owner,first[0])); Assert.True(calls.CanInvokePropCallback(owner,next[0]));
        calls.Dispose(); Assert.False(calls.CanInvokePropCallback(owner,next[0]));
    }
    [Fact]
    public void Pending_reply_is_owned_bounded_and_consumed_exactly_once()
    {
        var owner=Binding(); var sibling=Binding(); var calls=new PluginSandboxOwnedCalls(owner,1,1,1);
        Assert.True(calls.TryAddPendingReply(out var reply)); Assert.False(calls.TryAddPendingReply(out _));
        Assert.False(calls.TakePendingReply(sibling,reply)); Assert.True(calls.TakePendingReply(owner,reply));
        Assert.False(calls.TakePendingReply(owner,reply)); calls.Dispose();
    }
    [Fact]
    public void Stream_accepts_multiple_owned_chunks_until_end()
    {
        var owner=Binding(); var sibling=Binding(); var calls=new PluginSandboxOwnedCalls(owner,1,1,1);
        Assert.True(calls.TryBeginStream(out var stream)); Assert.False(calls.TryBeginStream(out _));
        Assert.False(calls.AcceptStreamChunk(sibling,stream)); Assert.True(calls.AcceptStreamChunk(owner,stream));
        Assert.True(calls.AcceptStreamChunk(owner,stream)); Assert.False(calls.EndStream(sibling,stream));
        Assert.True(calls.EndStream(owner,stream)); Assert.False(calls.AcceptStreamChunk(owner,stream));
        Assert.False(calls.EndStream(owner,stream)); calls.Dispose();
    }
    [Fact]
    public async Task Revocation_and_dispose_remove_all_owned_capabilities()
    {
        var owner=Binding(); var calls=new PluginSandboxOwnedCalls(owner,2,2,1);
        Assert.True(calls.TryBeginStream(out var stream)); Assert.True(calls.TryReplacePropCallbacks(1,out var callbacks)); Assert.True(calls.TryAddPendingReply(out var pending));
        await owner.DisposeAsync();
        Assert.False(calls.AcceptStreamChunk(owner,stream)); Assert.False(calls.CanInvokePropCallback(owner,callbacks[0])); Assert.False(calls.TakePendingReply(owner,pending));
        calls.Dispose(); Assert.False(calls.TryBeginStream(out _));
    }
    [Fact]
    public void Host_issuer_survives_many_sequential_operations_and_never_resurrects_retired_ids()
    {
        var owner=Binding(); using var calls=new PluginSandboxOwnedCalls(owner,1,1,1);
        Assert.True(calls.TryReplacePropCallbacks(1,out var oldCallbacks));
        Assert.True(calls.TryAddPendingReply(out var oldReply)); Assert.True(calls.TakePendingReply(owner,oldReply));
        Assert.True(calls.TryBeginStream(out var oldStream)); Assert.True(calls.EndStream(owner,oldStream));
        for (var i=0; i<5000; i++)
        {
            Assert.True(calls.TryReplacePropCallbacks(1,out var current));
            Assert.True(calls.TryAddPendingReply(out var reply)); Assert.True(calls.TakePendingReply(owner,reply));
            Assert.True(calls.TryBeginStream(out var stream)); Assert.True(calls.EndStream(owner,stream));
            Assert.NotEqual(oldCallbacks[0],current[0],StringComparer.Ordinal); Assert.NotEqual(oldReply,reply,StringComparer.Ordinal); Assert.NotEqual(oldStream,stream,StringComparer.Ordinal);
            Assert.False(calls.CanInvokePropCallback(owner,oldCallbacks[0])); Assert.False(calls.TakePendingReply(owner,oldReply)); Assert.False(calls.AcceptStreamChunk(owner,oldStream));
        }
    }
}
