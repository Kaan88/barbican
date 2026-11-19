// Source-derived ownership acceptance: Bulwark host-bridge.ts:318-322,614-666,740-771; M6b account/revocation limits.
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
    public void Callback_ids_are_owned_bounded_and_cannot_be_replayed_by_a_sibling()
    {
        var owner=Binding(); var sibling=Binding(); var calls=new PluginSandboxOwnedCalls(owner,1,1);
        Assert.True(calls.AddCallback("one")); Assert.False(calls.AddCallback("two"));
        Assert.False(calls.TakeCallback(sibling,"one")); Assert.True(calls.TakeCallback(owner,"one")); Assert.False(calls.TakeCallback(owner,"one"));
        calls.Dispose();
    }
    [Red("m6b-runtime")]
    [Fact]
    public async Task Revocation_and_dispose_remove_stream_and_callback_capabilities()
    {
        var owner=Binding(); var calls=new PluginSandboxOwnedCalls(owner,2,1);
        Assert.True(calls.AddStream("stream")); Assert.True(calls.AddCallback("callback"));
        await owner.DisposeAsync();
        Assert.False(calls.TakeStream(owner,"stream")); Assert.False(calls.TakeCallback(owner,"callback"));
        calls.Dispose(); Assert.False(calls.AddStream("later"));
    }
}
