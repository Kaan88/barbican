// Ported security expectations: Bulwark host-bridge.ts:427-477,740-771; protocol.ts:115-334; M6b-plan Security invariants.
using System.Text;
using System.Text.Json;
using Barbican.App.Plugins.Sandbox;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;

namespace Barbican.Automation.Tests.Plugins;

public sealed class PluginSandboxIngressRedTests
{
    private static PluginRuntimeBinding Binding()
    {
        var package = new PluginPackageIdentity("fixture", "1.0.0", new string('a', 64));
        return PluginRuntimeBinding.Create("auto-8931", new PluginGrantSnapshot(package, 1, true, ["storage:read"], []), PluginFrameKind.Background);
    }
    private sealed class Authority(PluginRuntimeBinding binding) : IPluginAuthority
    {
        public PluginGrantSnapshot Current { get; set; } = new(binding.Package,binding.GrantGeneration,true,["storage:read"],[],binding.Tier);
        public event EventHandler<PluginAuthorityChangedEventArgs>? Changed { add { } remove { } }
        public Task<Barbican.Core.Common.Result<PluginGrantSnapshot?>> GetCurrentAsync(string pluginId, CancellationToken ct) =>
            Task.FromResult(Barbican.Core.Common.Result<PluginGrantSnapshot?>.Ok(Current));
    }
    private static PluginSandboxSession Session(PluginRuntimeBinding binding, View view, Dispatcher dispatch) => new(binding,view,dispatch,new Authority(binding));
    private static byte[] Api(string extra = "")
    {
        using var json = JsonDocument.Parse("{\"type\":\"api-request\",\"id\":\"one\",\"method\":\"storage.get\",\"args\":[]" + extra + "}");
        return PluginValueCodec.Encode(PluginValue.FromJson(json.RootElement));
    }
    private static async Task InitializeAsync(PluginSandboxSession session, View view)
    {
        using var ready = JsonDocument.Parse("{\"type\":\"sandbox-ready\"}");
        var bytes = PluginValueCodec.Encode(PluginValue.FromJson(ready.RootElement));
        Assert.True(await session.ReceiveAsync(view.RootIdentity,true,view.TrustedRoot,() => bytes,TestContext.Current.CancellationToken));
        using var init = JsonDocument.Parse("{\"type\":\"init\",\"payload\":{\"mode\":\"background\",\"pluginId\":\"fixture\",\"tier\":\"untrusted\",\"manifest\":{\"id\":\"fixture\",\"version\":\"1.0.0\",\"permissions\":[],\"settings\":{}},\"code\":\"\",\"locale\":\"en\"}}");
        Assert.True(await session.IssueInitAsync(PluginValue.FromJson(init.RootElement),TestContext.Current.CancellationToken));
    }
    private sealed class View : IPluginSandboxWebView
    {
        public object RootIdentity { get; } = new();
        public Uri TrustedRoot { get; } = new("https://plugin.invalid/host/index.html");
        public List<byte[]> Sent { get; } = [];
        public Task NavigateAsync(Uri uri, CancellationToken ct) => Task.CompletedTask;
        public Task PostAsync(ReadOnlyMemory<byte> message, CancellationToken ct) { Sent.Add(message.ToArray()); return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Dispatcher : IPluginApiDispatcher
    {
        public Func<Task<PluginCallOutcome>>? Outcome { get; set; }
        public List<PluginRuntimeBinding> Seen { get; } = [];
        public string Account { get; set; } = "account-a";
        public List<string> Accounts { get; } = [];
        public Task<PluginCallOutcome> DispatchAsync(PluginRuntimeBinding binding, PluginApiRequest request, CancellationToken ct)
        { Seen.Add(binding); Accounts.Add(Account); return Outcome?.Invoke() ?? Task.FromResult(PluginCallOutcome.Succeeded()); }
    }

    [Fact]
    public async Task Api_is_rejected_before_ready_but_allowed_during_activation_after_init_is_issued()
    {
        var view = new View(); var dispatch = new Dispatcher(); var session = Session(Binding(),view,dispatch);
        Assert.False(await session.ReceiveAsync(view.RootIdentity,true,view.TrustedRoot,() => Api(),TestContext.Current.CancellationToken));
        Assert.Empty(dispatch.Seen);
        await InitializeAsync(session,view);
        Assert.True(await session.ReceiveAsync(view.RootIdentity,true,view.TrustedRoot,() => Api(),TestContext.Current.CancellationToken));
        Assert.Single(dispatch.Seen);
    }

    [Fact]
    public async Task Direct_iframe_message_is_rejected_without_reading_its_payload()
    {
        var view = new View(); var dispatch = new Dispatcher(); var session = Session(Binding(), view, dispatch);
        await InitializeAsync(session, view);
        var read = false;
        Assert.False(await session.ReceiveAsync(view.RootIdentity, false, view.TrustedRoot, () => { read = true; return Api(); }, TestContext.Current.CancellationToken));
        Assert.False(read); Assert.Empty(dispatch.Seen);
    }

    [Fact]
    public async Task Another_webview_or_top_level_route_cannot_claim_this_instance()
    {
        var view = new View(); var dispatch = new Dispatcher(); var session = Session(Binding(), view, dispatch);
        await InitializeAsync(session, view);
        Assert.False(await session.ReceiveAsync(new object(), true, view.TrustedRoot, () => Api(), TestContext.Current.CancellationToken));
        Assert.False(await session.ReceiveAsync(view.RootIdentity, true, new Uri("https://plugin.invalid/other"), () => Api(), TestContext.Current.CancellationToken));
        Assert.Empty(dispatch.Seen);
    }

    [Fact]
    public async Task Authenticated_top_level_dispatch_uses_host_created_binding()
    {
        var view = new View(); var dispatch = new Dispatcher(); var binding = Binding(); var session = Session(binding, view, dispatch);
        await InitializeAsync(session, view);
        Assert.True(await session.ReceiveAsync(view.RootIdentity, true, view.TrustedRoot, () => Api(), TestContext.Current.CancellationToken));
        Assert.Same(binding, Assert.Single(dispatch.Seen));
    }

    [Fact]
    public async Task Background_frame_uses_dispatcher_account_capture_for_each_call()
    {
        var view = new View(); var dispatch = new Dispatcher(); var binding = Binding(); var session = Session(binding, view, dispatch);
        await InitializeAsync(session, view);
        Assert.True(await session.ReceiveAsync(view.RootIdentity, true, view.TrustedRoot, () => Api(), TestContext.Current.CancellationToken));
        dispatch.Account = "account-b";
        using var json = JsonDocument.Parse("{\"type\":\"api-request\",\"id\":\"two\",\"method\":\"storage.get\",\"args\":[]}");
        var next = PluginValueCodec.Encode(PluginValue.FromJson(json.RootElement));
        Assert.True(await session.ReceiveAsync(view.RootIdentity, true, view.TrustedRoot, () => next, TestContext.Current.CancellationToken));
        Assert.Equal(["account-a", "account-b"], dispatch.Accounts);
        Assert.All(dispatch.Seen, x => Assert.Same(binding, x));
    }

    [Fact]
    public async Task Claimed_authority_fields_cannot_replace_native_binding()
    {
        var view = new View(); var dispatch = new Dispatcher(); var binding = Binding(); var session = Session(binding, view, dispatch);
        await InitializeAsync(session, view);
        var bytes = Api(",\"pluginId\":\"other\",\"grantGeneration\":999");
        var accepted = await session.ReceiveAsync(view.RootIdentity, true, view.TrustedRoot, () => bytes, TestContext.Current.CancellationToken);
        Assert.True(!accepted || dispatch.Seen.All(x => ReferenceEquals(x, binding)));
    }

    [Fact]
    public async Task Navigation_start_invalidates_binding_before_any_queued_message_can_dispatch()
    {
        var view = new View(); var dispatch = new Dispatcher(); var binding = Binding(); var session = Session(binding, view, dispatch);
        await InitializeAsync(session, view);
        var cleanup = session.NavigationStartingAsync(view.RootIdentity, TestContext.Current.CancellationToken);
        Assert.False(binding.IsAlive);
        Assert.False(await session.ReceiveAsync(view.RootIdentity, true, view.TrustedRoot, () => Api(), TestContext.Current.CancellationToken));
        await cleanup; Assert.Empty(dispatch.Seen);
    }

    [Fact]
    public async Task Revoked_or_disposed_instances_never_dispatch_late_messages()
    {
        var view = new View(); var dispatch = new Dispatcher(); var binding = Binding(); var session = Session(binding, view, dispatch);
        await InitializeAsync(session, view);
        await binding.DisposeAsync();
        Assert.False(await session.ReceiveAsync(view.RootIdentity, true, view.TrustedRoot, () => Api(), TestContext.Current.CancellationToken));
        Assert.Empty(dispatch.Seen);
    }

    [Fact]
    public async Task Malformed_native_messages_do_not_reach_dispatcher()
    {
        var view = new View(); var dispatch = new Dispatcher(); var session = Session(Binding(), view, dispatch);
        await InitializeAsync(session, view);
        Assert.False(await session.ReceiveAsync(view.RootIdentity, true, view.TrustedRoot, () => Encoding.UTF8.GetBytes("{"), TestContext.Current.CancellationToken));
        Assert.Empty(dispatch.Seen);
    }

    [Fact]
    public async Task Valid_encoded_message_over_the_native_limit_does_not_reach_dispatcher()
    {
        var view = new View(); var dispatch = new Dispatcher(); var session = Session(Binding(), view, dispatch);
        var b = new PluginValueBuilder();
        var args = b.Array(b.String(new string('x', 1_100_000)));
        var root = b.Object(("type",b.String("api-request")), ("id",b.String("large")), ("method",b.String("storage.get")), ("args",args));
        var limits = new PluginValueLimits(MaxWireBytes: 4_000_000, MaxStringBytes: 4_000_000);
        var bytes = PluginValueCodec.Encode(b.Build(root, limits), limits);
        Assert.True(bytes.Length > new PluginValueLimits().MaxWireBytes);
        await InitializeAsync(session, view);
        Assert.False(await session.ReceiveAsync(view.RootIdentity, true, view.TrustedRoot, () => bytes, TestContext.Current.CancellationToken));
        Assert.Empty(dispatch.Seen);
    }
    [Fact]
    public async Task Api_response_preserves_binary_and_aliases()
    {
        var view=new View(); var dispatch=new Dispatcher(); var builder=new PluginValueBuilder();
        var buffer=builder.Buffer(new byte[] { 1,2,3 }); var array=builder.Array(buffer,buffer);
        dispatch.Outcome=() => Task.FromResult(PluginCallOutcome.Succeeded(builder.Build(array)));
        var session=Session(Binding(),view,dispatch); await InitializeAsync(session,view);
        Assert.True(await session.ReceiveAsync(view.RootIdentity,true,view.TrustedRoot,() => Api(),TestContext.Current.CancellationToken));
        var response=PluginValueCodec.Decode(view.Sent[^1]); var result=response.Property("result");
        Assert.True(result.Item(0).SameNode(result.Item(1))); Assert.Equal(new byte[] {1,2,3},result.Item(0).BinaryBytes());
    }
    [Fact]
    public async Task Revocation_while_dispatch_is_pending_suppresses_response()
    {
        var view=new View(); var dispatch=new Dispatcher(); var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release=new TaskCompletionSource<PluginCallOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
#pragma warning disable VSTHRD003 // Deliberately controlled completion models in-flight dispatch.
        dispatch.Outcome=() => { started.SetResult(); return release.Task; };
#pragma warning restore VSTHRD003
        var session=Session(Binding(),view,dispatch); await InitializeAsync(session,view); var before=view.Sent.Count;
        var call=session.ReceiveAsync(view.RootIdentity,true,view.TrustedRoot,() => Api(),TestContext.Current.CancellationToken);
        await started.Task; await session.NavigationStartingAsync(view.RootIdentity,TestContext.Current.CancellationToken);
        release.SetResult(PluginCallOutcome.Succeeded()); Assert.False(await call); Assert.Equal(before,view.Sent.Count);
    }

    [Fact]
    public async Task Changed_grant_during_dispatch_suppresses_response()
    {
        var view=new View(); var dispatch=new Dispatcher(); var binding=Binding(); var authority=new Authority(binding);
        var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release=new TaskCompletionSource<PluginCallOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
#pragma warning disable VSTHRD003 // Deliberately controlled completion models in-flight dispatch.
        dispatch.Outcome=() => { started.SetResult(); return release.Task; };
#pragma warning restore VSTHRD003
        var session=new PluginSandboxSession(binding,view,dispatch,authority); await InitializeAsync(session,view); var before=view.Sent.Count;
        var call=session.ReceiveAsync(view.RootIdentity,true,view.TrustedRoot,() => Api(),TestContext.Current.CancellationToken);
        await started.Task; authority.Current=new PluginGrantSnapshot(binding.Package,binding.GrantGeneration+1,true,["storage:read"],[]);
        release.SetResult(PluginCallOutcome.Succeeded()); Assert.False(await call); Assert.Equal(before,view.Sent.Count);
    }
    [Fact]
    public async Task Changed_grant_before_request_prevents_dispatch()
    {
        var view=new View(); var dispatch=new Dispatcher(); var binding=Binding(); var authority=new Authority(binding);
        var session=new PluginSandboxSession(binding,view,dispatch,authority); await InitializeAsync(session,view);
        authority.Current=new PluginGrantSnapshot(binding.Package,binding.GrantGeneration+1,true,["storage:read"],[]);
        Assert.False(await session.ReceiveAsync(view.RootIdentity,true,view.TrustedRoot,() => Api(),TestContext.Current.CancellationToken)); Assert.Empty(dispatch.Seen);
    }

}

