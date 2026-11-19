// Source-derived ownership acceptance: Bulwark host-bridge.ts:318-322,521-541,614-666,718-726,740-763.
using Barbican.App.Plugins.Sandbox;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Runtime;
namespace Barbican.Automation.Tests.Plugins;
public sealed class PluginSandboxOwnedCallsRedTests
{
    private static PluginRuntimeBinding Binding() => PluginRuntimeBinding.Create("auto-8931", new PluginGrantSnapshot(new PluginPackageIdentity("fixture","1.0.0",new string('a',64)),1,true,[],[]), PluginFrameKind.Background);
    [Red("m6b-runtime")]
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
    [Red("m6b-runtime")]
    [Fact]
    public void Pending_reply_is_owned_bounded_and_consumed_exactly_once()
    {
        var owner=Binding(); var sibling=Binding(); var calls=new PluginSandboxOwnedCalls(owner,1,1,1);
        Assert.True(calls.AddPendingReply("reply")); Assert.False(calls.AddPendingReply("other"));
        Assert.False(calls.TakePendingReply(sibling,"reply")); Assert.True(calls.TakePendingReply(owner,"reply"));
        Assert.False(calls.TakePendingReply(owner,"reply")); calls.Dispose();
    }
    [Red("m6b-runtime")]
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
    [Red("m6b-runtime")]
    [Fact]
    public async Task Revocation_and_dispose_remove_all_owned_capabilities()
    {
        var owner=Binding(); var calls=new PluginSandboxOwnedCalls(owner,2,2,1);
        Assert.True(calls.BeginStream("stream")); Assert.True(calls.ReplacePropCallbacks(["callback"])); Assert.True(calls.AddPendingReply("pending"));
        await owner.DisposeAsync();
        Assert.False(calls.AcceptStreamChunk(owner,"stream")); Assert.False(calls.CanInvokePropCallback(owner,"callback")); Assert.False(calls.TakePendingReply(owner,"pending"));
        calls.Dispose(); Assert.False(calls.BeginStream("later"));
    }
}
