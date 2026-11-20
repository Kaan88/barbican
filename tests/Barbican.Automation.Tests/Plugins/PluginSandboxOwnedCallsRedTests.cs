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
        Assert.True(calls.ReplacePropCallbacks(["first"]));
        Assert.False(calls.CanInvokePropCallback(sibling,"first"));
        Assert.True(calls.CanInvokePropCallback(owner,"first")); Assert.True(calls.CanInvokePropCallback(owner,"first"));
        Assert.False(calls.ReplacePropCallbacks(["a","b"]));
        Assert.True(calls.CanInvokePropCallback(owner,"first")); // failed replacement is atomic
        Assert.True(calls.ReplacePropCallbacks(["next"]));
        Assert.False(calls.CanInvokePropCallback(owner,"first")); Assert.True(calls.CanInvokePropCallback(owner,"next"));
        calls.Dispose(); Assert.False(calls.CanInvokePropCallback(owner,"next"));
    }
    [Fact]
    public void Pending_reply_is_owned_bounded_and_consumed_exactly_once()
    {
        var owner=Binding(); var sibling=Binding(); var calls=new PluginSandboxOwnedCalls(owner,1,1,1);
        Assert.True(calls.AddPendingReply("reply")); Assert.False(calls.AddPendingReply("other"));
        Assert.False(calls.TakePendingReply(sibling,"reply")); Assert.True(calls.TakePendingReply(owner,"reply"));
        Assert.False(calls.TakePendingReply(owner,"reply")); calls.Dispose();
    }
    [Fact]
    public void Stream_accepts_multiple_owned_chunks_until_end()
    {
        var owner=Binding(); var sibling=Binding(); var calls=new PluginSandboxOwnedCalls(owner,1,1,1);
        Assert.True(calls.BeginStream("stream")); Assert.False(calls.BeginStream("other"));
        Assert.False(calls.AcceptStreamChunk(sibling,"stream"));
        Assert.True(calls.AcceptStreamChunk(owner,"stream")); Assert.True(calls.AcceptStreamChunk(owner,"stream"));
        Assert.False(calls.EndStream(sibling,"stream")); Assert.True(calls.EndStream(owner,"stream"));
        Assert.False(calls.AcceptStreamChunk(owner,"stream")); Assert.False(calls.EndStream(owner,"stream")); calls.Dispose();
    }
    [Fact]
    public async Task Revocation_and_dispose_remove_all_owned_capabilities()
    {
        var owner=Binding(); var calls=new PluginSandboxOwnedCalls(owner,2,2,1);
        Assert.True(calls.BeginStream("stream")); Assert.True(calls.ReplacePropCallbacks(["callback"])); Assert.True(calls.AddPendingReply("pending"));
        await owner.DisposeAsync();
        Assert.False(calls.AcceptStreamChunk(owner,"stream")); Assert.False(calls.CanInvokePropCallback(owner,"callback")); Assert.False(calls.TakePendingReply(owner,"pending"));
        calls.Dispose(); Assert.False(calls.BeginStream("later"));
    }
    [Fact]
    public void Invalid_ids_and_disposal_fail_closed()
    {
        var owner = Binding();
        using var calls = new PluginSandboxOwnedCalls(owner, 3, 1, 1);
        Assert.True(calls.ReplacePropCallbacks(["first"]));
        Assert.False(calls.ReplacePropCallbacks(["next", ""]));
        Assert.True(calls.CanInvokePropCallback(owner, "first"));
        Assert.False(calls.AddPendingReply(""));
        Assert.False(calls.BeginStream(new string('x', 1025)));
        Assert.True(calls.AddPendingReply("reply"));
        Assert.False(calls.AddPendingReply("reply"));
        Assert.True(calls.TakePendingReply(owner, "reply"));
        Assert.True(calls.AddPendingReply("next"));
        calls.Dispose();
        Assert.False(calls.TakePendingReply(owner, "next"));
        Assert.False(calls.ReplacePropCallbacks([]));
    }
    [Fact]
    public void Retired_ids_and_invalid_ids_cannot_be_resurrected()
    {
        var owner = Binding(); using var calls = new PluginSandboxOwnedCalls(owner, 2, 2, 2);
        Assert.True(calls.ReplacePropCallbacks(["old"])); Assert.True(calls.ReplacePropCallbacks(["new"]));
        Assert.False(calls.ReplacePropCallbacks(["old"])); Assert.True(calls.CanInvokePropCallback(owner, "new"));
        Assert.True(calls.AddPendingReply("reply")); Assert.True(calls.TakePendingReply(owner, "reply")); Assert.False(calls.AddPendingReply("reply"));
        Assert.True(calls.BeginStream("stream")); Assert.True(calls.EndStream(owner, "stream")); Assert.False(calls.BeginStream("stream"));
        Assert.False(calls.AddPendingReply("bad\nvalue")); Assert.False(calls.BeginStream(new string('x', 257)));
    }
    [Fact]
    public void Hook_ids_use_bounded_monotonic_history_and_reserved_namespace()
    {
        var owner = Binding(); using var calls = new PluginSandboxOwnedCalls(owner, 0, 1, 0);
        Assert.False(calls.AddPendingReply($"{owner.InstanceId:N}:h:1"));
        for (long sequence = 1; sequence <= 70_000; sequence++)
        {
            Assert.True(calls.AddHookReply(sequence, out var id));
            Assert.True(calls.TakePendingReply(owner, id));
        }
        Assert.False(calls.AddHookReply(1, out _));
        Assert.False(calls.AddPendingReply($"{owner.InstanceId:N}:h:1"));
        Assert.True(calls.AddHookReply(70_001, out _));
    }
    [Fact]
    public void External_id_history_fails_closed_at_its_bound()
    {
        var owner = Binding(); using var calls = new PluginSandboxOwnedCalls(owner, 1, 1, 1);
        for (var i = 0; i < 1024; i++)
        {
            var id = $"reply-{i}";
            Assert.True(calls.AddPendingReply(id));
            Assert.True(calls.TakePendingReply(owner, id));
        }
        Assert.False(calls.AddPendingReply("new"));
        Assert.False(calls.AddPendingReply("reply-0"));
    }
}
