// Ported security expectations: Bulwark host-bridge.ts:427-477,740-771; protocol.ts:115-334; M6b-plan Security invariants.
using System.Text;
using System.Text.Json;
using Barbican.App.Plugins.Sandbox;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Mail;
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
        return PluginRuntimeBinding.Create("auto-8931", new PluginGrantSnapshot(package, 1, true, ["storage:read"], ["a", "b"]), PluginFrameKind.Background);
    }
    private sealed class Authority(PluginRuntimeBinding binding) : IPluginDeliveryAuthority
    {
        public PluginGrantSnapshot Current { get; set; } = new(binding.Package,binding.GrantGeneration,true,["storage:read"],["a", "b"],binding.Tier);
        public event EventHandler<PluginAuthorityChangedEventArgs>? Changed { add { } remove { } }
        public Task<Barbican.Core.Common.Result<PluginGrantSnapshot?>> GetCurrentAsync(string pluginId, CancellationToken ct) =>
            Task.FromResult(Barbican.Core.Common.Result<PluginGrantSnapshot?>.Ok(Current));

        public Task<bool> TryPostAsync(PluginRuntimeBinding recipient, Func<PluginGrantSnapshot, bool> write, CancellationToken ct)
        {
            var grant = Current;
            return Task.FromResult(!ct.IsCancellationRequested && grant.Enabled && grant.Package == recipient.Package
                && grant.Generation == recipient.GrantGeneration && grant.Tier == recipient.Tier && write(grant));
        }
    }
    private static PluginSandboxSession Session(PluginRuntimeBinding binding, View view, Dispatcher dispatch) => new(binding,view,dispatch,new Authority(binding));
    private static byte[] Api(string extra = "")
    {
        using var json = JsonDocument.Parse("{\"type\":\"api-request\",\"id\":\"one\",\"method\":\"storage.get\",\"args\":[]" + extra + "}");
        return PluginValueCodec.Encode(PluginValue.FromJson(json.RootElement));
    }
    private static byte[] Message(string json)
    {
        using var document = JsonDocument.Parse(json);
        return PluginValueCodec.Encode(PluginValue.FromJson(document.RootElement));
    }
    private const string InitDone = "{\"type\":\"init-done\",\"hooks\":[],\"slots\":[],\"shortcuts\":[]}";
    private const string InitError = "{\"type\":\"init-error\",\"error\":\"failed\"}";

    [Fact]
    public async Task Hook_replies_require_the_authenticated_parent_and_a_live_grant()
    {
        var view = new View();
        var binding = Binding();
        var authority = new Authority(binding);
        await using var session = new PluginSandboxSession(binding, view, new Dispatcher(), authority);
        var ct = TestContext.Current.CancellationToken;
        var builder = new PluginValueBuilder();
        var arguments = builder.Build(builder.Array(builder.String("fixture")));

        Assert.False((await session.InvokeHookAsync("fixture-hook", arguments, TimeSpan.FromSeconds(5), ct)).IsOk);
        await InitializeAsync(session, view);
        Assert.True(await session.ReceiveAsync(view.RootIdentity, true, view.TrustedRoot, () => Message(InitDone), ct));
        var pending = session.InvokeHookAsync("fixture-hook", arguments, TimeSpan.FromSeconds(5), ct);
        var request = PluginValueCodec.Decode(view.Sent[^1]);
        Assert.Equal("hook-invoke", request.Property("type").StringValue);
        Assert.Equal("fixture", request.Property("args").Item(0).StringValue);
        var reply = Message($$"""{"type":"hook-result","id":"{{request.Property("id").StringValue}}","ok":true,"result":"response"}""");
        Assert.False(await session.ReceiveAsync(view.RootIdentity, false, view.TrustedRoot, () => reply, ct));
        Assert.True(await session.ReceiveAsync(view.RootIdentity, true, view.TrustedRoot, () => reply, ct));
        Assert.Equal("response", (await pending).Value!.StringValue);
        Assert.False(await session.ReceiveAsync(view.RootIdentity, true, view.TrustedRoot, () => reply, ct));

        var revoked = session.InvokeHookAsync("fixture-hook", arguments, TimeSpan.FromSeconds(5), ct);
        request = PluginValueCodec.Decode(view.Sent[^1]);
        reply = Message($$"""{"type":"hook-result","id":"{{request.Property("id").StringValue}}","ok":true,"result":"revoked"}""");
        authority.Current = new(binding.Package, binding.GrantGeneration + 1, true, ["storage:read"], ["a", "b"], binding.Tier);
        Assert.True(await session.ReceiveAsync(view.RootIdentity, true, view.TrustedRoot, () => reply, ct));
        Assert.False((await revoked).IsOk);
    }

    [Fact]
    public async Task A_failed_native_hook_post_returns_a_generic_transport_error()
    {
        var view = new View();
        await using var session = Session(Binding(), view, new Dispatcher());
        var ct = TestContext.Current.CancellationToken;
        var builder = new PluginValueBuilder();
        await InitializeAsync(session, view);
        Assert.True(await session.ReceiveAsync(view.RootIdentity, true, view.TrustedRoot, () => Message(InitDone), ct));
#pragma warning disable CA2201 // Simulate the exception raised by the native WebView transport.
        view.BeforeDelivery = static () => throw new System.Runtime.InteropServices.COMException("private native detail");
#pragma warning restore CA2201

        var result = await session.InvokeHookAsync("fixture-hook", builder.Build(builder.Array()), TimeSpan.FromSeconds(5), ct);

        Assert.IsType<Barbican.Core.Jmap.Errors.TransportError>(result.Error);
        Assert.DoesNotContain("private native detail", result.Error!.Message, StringComparison.Ordinal);
        Assert.Single(view.Sent);
    }

    [Fact]
    public async Task Initialization_acknowledgements_require_authenticated_issued_init_and_accept_only_one_outcome()
    {
        var view = new View();
        await using var session = Session(Binding(), view, new Dispatcher());
        var ct = TestContext.Current.CancellationToken;
        var ready = session.WaitForReadyAsync(ct);
        var initialized = session.WaitForInitializationAsync(ct);
        var done = Message(InitDone);
        Assert.False(await session.ReceiveAsync(view.RootIdentity, true, view.TrustedRoot, () => done, ct));
        Assert.False(await session.ReceiveAsync(view.RootIdentity, false, view.TrustedRoot, () => Message("{\"type\":\"sandbox-ready\"}"), ct));
        Assert.False(ready.IsCompleted);
        await InitializeAsync(session, view);
        await ready;
        Assert.False(await session.ReceiveAsync(new object(), true, view.TrustedRoot, () => done, ct));
        Assert.False(initialized.IsCompleted);
        Assert.True(await session.ReceiveAsync(view.RootIdentity, true, view.TrustedRoot, () => done, ct));
        Assert.Equal("init-done", (await initialized)!.Property("type").StringValue);
        Assert.False(await session.ReceiveAsync(view.RootIdentity, true, view.TrustedRoot, () => Message(InitError), ct));
        Assert.False(await session.ReceiveAsync(view.RootIdentity, true, view.TrustedRoot, () => done, ct));
    }

    [Fact]
    public async Task Initialization_error_closes_ingress_without_losing_the_failed_outcome()
    {
        var view = new View();
        var dispatch = new Dispatcher();
        await using var session = Session(Binding(), view, dispatch);
        var ct = TestContext.Current.CancellationToken;
        await InitializeAsync(session, view);
        var waiting = session.WaitForInitializationAsync(ct);
        Assert.True(await session.ReceiveAsync(view.RootIdentity, true, view.TrustedRoot, () => Message(InitError), ct));
        Assert.False(session.Binding.IsAlive);
        Assert.Null(await waiting);
        Assert.Null(await session.WaitForInitializationAsync(ct));
        Assert.False(await session.ReceiveAsync(view.RootIdentity, true, view.TrustedRoot, () => Message(InitDone), ct));
        Assert.False(await session.ReceiveAsync(view.RootIdentity, true, view.TrustedRoot, () => Api(), ct));
        Assert.Empty(dispatch.Seen);
        Assert.Single(view.Sent);
    }

    [Fact]
    public async Task Startup_waits_cancel_with_caller_or_binding_without_affecting_other_waiters()
    {
        var view = new View();
        var binding = Binding();
        await using var session = Session(binding, view, new Dispatcher());
        using var cancel = new CancellationTokenSource();
        var canceled = session.WaitForReadyAsync(cancel.Token);
        var ready = session.WaitForReadyAsync(TestContext.Current.CancellationToken);
        var initialized = session.WaitForInitializationAsync(TestContext.Current.CancellationToken);
        await cancel.CancelAsync();
#pragma warning disable VSTHRD003 // The test starts each waiter above so it can observe cancellation while they are pending.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
        Assert.False(ready.IsCompleted);
        await binding.DisposeAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ready);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => initialized);
#pragma warning restore VSTHRD003
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
        public Action? BeforeDelivery { get; set; }
        public Task NavigateAsync(Uri uri, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> PostAsync(Func<Action<string>, CancellationToken, ValueTask<bool>> deliver, CancellationToken ct)
        {
            BeforeDelivery?.Invoke();
            return deliver(message => Sent.Add(Encoding.UTF8.GetBytes(message)), ct).AsTask();
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Dispatcher : IPluginApiDispatcher
    {
        public Func<Task<PluginCallOutcome>>? Outcome { get; set; }
        public List<PluginRuntimeBinding> Seen { get; } = [];
        public string Account { get; set; } = "account-a";
        public List<string> Accounts { get; } = [];
        public Task<PluginCallOutcome> DispatchAsync(PluginRuntimeBinding binding, PluginApiRequest request, CancellationToken ct)
        { Seen.Add(binding); Accounts.Add(Account); return Outcome?.Invoke() ?? Task.FromResult(PluginCallOutcome.Failed(PluginErrorCode.Unavailable)); }
    }

    private sealed class Method(Func<IPluginInvocationContext, PluginCallOutcome> invoke) : IPluginApiMethod
    {
        public PluginApiDescriptor Descriptor { get; } = new("storage.get", [["storage:read"]], TimeSpan.FromSeconds(5));
        public PluginErrorCode? ValidateArguments(PluginValue arguments) => null;
        public Task<PluginCallOutcome> InvokeAsync(IPluginInvocationContext context, PluginValue arguments, CancellationToken ct) => Task.FromResult(invoke(context));
    }

    private sealed class Sessions : IPluginSessionSource
    {
        private IAccountSession _active = new FakeSession("a");
        private long _epoch = 1;
        public void Switch() { _active = new FakeSession("b"); _epoch++; }
        public Result<PluginSessionCapture> CaptureActive() => Result<PluginSessionCapture>.Ok(new(_active, _epoch));
        public Result<PluginSessionCapture> CaptureOwner(string localAccountKey) => throw new NotSupportedException();
        public bool IsCurrent(PluginSessionCapture capture) => ReferenceEquals(_active, capture.Session) && _epoch == capture.ActiveSelectionGeneration;
        public bool TryRunWhileCurrent(IReadOnlyList<PluginSessionCapture> captures, Func<bool> write) => captures.All(IsCurrent) && write();
    }

    private sealed class FakeSession(string key) : IAccountSession
    {
        public AccountInfo Account { get; } = new(key, "sample@example.org", "https://jmap.example.org");
        public IJmapClient Client => throw new NotSupportedException();
        public IMailService Mail => throw new NotSupportedException();
        public IMailSync Sync => throw new NotSupportedException();
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
    public async Task OnlyTheTrustedParentCanSignalTransportShutdown()
    {
        var view = new View();
        var binding = Binding();
        await using var session = Session(binding, view, new Dispatcher());
        var bytes = "barbican-sandbox-transport-closed"u8.ToArray();
        var ct = TestContext.Current.CancellationToken;
        Assert.False(await session.ReceiveAsync(view.RootIdentity, false, view.TrustedRoot, () => bytes, ct));
        Assert.True(binding.IsAlive);

        Assert.True(await session.ReceiveAsync(view.RootIdentity, true, view.TrustedRoot, () => bytes, ct));
        Assert.False(binding.IsAlive);
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
        var view=new View(); var binding=Binding(); var authority=new Authority(binding); var builder=new PluginValueBuilder();
        var buffer=builder.Buffer(new byte[] { 1,2,3 }); var array=builder.Array(buffer,buffer);
        var dispatch=new PluginApiDispatcher(binding.Profile, [new Method(_ => PluginCallOutcome.Succeeded(builder.Build(array)))], authority, new Sessions());
        await using var session=new PluginSandboxSession(binding,view,dispatch,authority);
        await InitializeAsync(session,view);
        Assert.True(await session.ReceiveAsync(view.RootIdentity,true,view.TrustedRoot,() => Api(),TestContext.Current.CancellationToken));
        var response=PluginValueCodec.Decode(view.Sent[^1]); var result=response.Property("result");
        Assert.True(result.Item(0).SameNode(result.Item(1))); Assert.Equal(new byte[] {1,2,3},result.Item(0).BinaryBytes());
    }

    [Fact]
    public async Task Account_change_at_queued_delivery_withholds_result_and_next_call_uses_current_account()
    {
        var view = new View(); var binding = Binding(); var authority = new Authority(binding); var accounts = new Sessions();
        var method = new Method(context =>
        {
            var builder = new PluginValueBuilder();
            return PluginCallOutcome.Succeeded(builder.Build(builder.String(context.CaptureActive().Value!.LocalAccountKey)));
        });
        var dispatcher = new PluginApiDispatcher(binding.Profile, [method], authority, accounts);
        await using var session = new PluginSandboxSession(binding, view, dispatcher, authority);
        await InitializeAsync(session, view);
        var ct = TestContext.Current.CancellationToken;
        view.BeforeDelivery = accounts.Switch;
        Assert.True(await session.ReceiveAsync(view.RootIdentity, true, view.TrustedRoot, () => Api(), ct));
        var refused = PluginValueCodec.Decode(view.Sent[^1]);
        Assert.False(refused.Property("ok").BooleanValue);
        Assert.Equal("Plugin context changed; retry with current data.", refused.Property("error").StringValue);
        Assert.DoesNotContain("result", refused.PropertyNames, StringComparer.Ordinal);

        view.BeforeDelivery = null;
        Assert.True(await session.ReceiveAsync(view.RootIdentity, true, view.TrustedRoot, () => Api(), ct));
        Assert.Equal("b", PluginValueCodec.Decode(view.Sent[^1]).Property("result").StringValue);
    }

    [Fact]
    public async Task Grant_change_at_queued_delivery_writes_no_envelope()
    {
        var view = new View(); var binding = Binding(); var authority = new Authority(binding); var accounts = new Sessions();
        var dispatcher = new PluginApiDispatcher(binding.Profile, [new Method(_ => PluginCallOutcome.Succeeded())], authority, accounts);
        await using var session = new PluginSandboxSession(binding, view, dispatcher, authority);
        await InitializeAsync(session, view);
        var count = view.Sent.Count;
        view.BeforeDelivery = () => authority.Current = new(binding.Package, binding.GrantGeneration + 1, true, ["storage:read"], ["a", "b"]);
        Assert.False(await session.ReceiveAsync(view.RootIdentity, true, view.TrustedRoot, () => Api(), TestContext.Current.CancellationToken));
        Assert.Equal(count, view.Sent.Count);
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
