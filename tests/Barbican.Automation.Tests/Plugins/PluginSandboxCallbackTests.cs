// Bulwark lib/plugin-sandbox/host-bridge.ts:118-145 (a host function handed to a frame is a { __pluginCallback: id } marker held in the frame's table) and :521-537 (callback-invoke: found, run, answered; unknown id answers an error);
// Barbican fences: only the trusted parent, only after init, only while the grant is the one the frame started under, and only functions of this frame's own table.
using System.Text;
using System.Text.Json;
using Barbican.App.Plugins.Sandbox;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;

namespace Barbican.Automation.Tests.Plugins;

public sealed class PluginSandboxCallbackTests
{
    private const string InitDone = "{\"type\":\"init-done\",\"hooks\":[],\"slots\":[],\"shortcuts\":[]}";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static PluginRuntimeBinding Binding()
    {
        var package = new PluginPackageIdentity("fixture", "1.0.0", new string('a', 64));
        return PluginRuntimeBinding.Create("auto-8931", new PluginGrantSnapshot(package, 1, true, ["ui:observe"], []), PluginFrameKind.Slot, "plugin-dialog");
    }

    private static byte[] Message(string json)
    {
        using var document = JsonDocument.Parse(json);
        return PluginValueCodec.Encode(PluginValue.FromJson(document.RootElement));
    }

    private static byte[] Invoke(string id, string callbackId, string args = "[]") =>
        Message($$"""{"type":"callback-invoke","id":"{{id}}","callbackId":"{{callbackId}}","args":{{args}}}""");

    private static async Task InitializeAsync(PluginSandboxSession session, View view, bool issueInit = true)
    {
        var ready = Message("{\"type\":\"sandbox-ready\"}");
        Assert.True(await session.ReceiveAsync(view.RootIdentity, true, view.TrustedRoot, () => ready, Ct));
        if (!issueInit) return;
        using var init = JsonDocument.Parse("{\"type\":\"init\",\"payload\":{\"mode\":\"slot\",\"pluginId\":\"fixture\",\"tier\":\"untrusted\",\"manifest\":{\"id\":\"fixture\",\"version\":\"1.0.0\",\"permissions\":[],\"settings\":{}},\"code\":\"\",\"locale\":\"en\",\"slot\":\"plugin-dialog\",\"extraProps\":{},\"theme\":{\"dark\":false,\"fontFamily\":\"x\",\"vars\":{}}}}");
        Assert.True(await session.IssueInitAsync(PluginValue.FromJson(init.RootElement), Ct));
        var done = Message(InitDone);
        Assert.True(await session.ReceiveAsync(view.RootIdentity, true, view.TrustedRoot, () => done, Ct));
    }

    private static PluginValue Last(View view) => PluginValueCodec.Decode(view.Sent[^1]);

    private static (PluginHostCallbacks Table, string Id, List<PluginValue> Calls) Table(Func<PluginValue, Task<PluginValue?>>? run = null)
    {
        var table = new PluginHostCallbacks();
        var calls = new List<PluginValue>();
        var id = table.Register((arguments, _) =>
        {
            calls.Add(arguments);
            return run?.Invoke(arguments) ?? Task.FromResult<PluginValue?>(null);
        });
        return (table, id, calls);
    }

    [Fact]
    public async Task A_callback_the_host_registered_is_run_with_the_frames_arguments_and_answered()
    {
        var view = new View();
        var binding = Binding();
        var (table, id, calls) = Table(arguments =>
        {
            var builder = new PluginValueBuilder();
            return Task.FromResult<PluginValue?>(builder.Build(builder.Boolean(true)));
        });
        await using var session = new PluginSandboxSession(binding, view, new Dispatcher(), new Authority(binding), table);
        await InitializeAsync(session, view);

        Assert.True(await session.ReceiveAsync(view.RootIdentity, true, view.TrustedRoot, () => Invoke("c1", id, "[{\"picked\":\"/docs/a.txt\"}]"), Ct));

        var call = Assert.Single(calls);
        Assert.Equal("/docs/a.txt", call.Item(0).Property("picked").StringValue, StringComparer.Ordinal);
        var response = Last(view);
        Assert.Equal("callback-response", response.Property("type").StringValue, StringComparer.Ordinal);
        Assert.Equal("c1", response.Property("id").StringValue, StringComparer.Ordinal);
        Assert.True(response.Property("ok").BooleanValue);
        Assert.True(response.Property("result").BooleanValue);
    }

    [Fact]
    public async Task A_callback_that_returns_nothing_answers_undefined_and_a_throwing_one_answers_a_generic_error()
    {
        var view = new View();
        var binding = Binding();
        var table = new PluginHostCallbacks();
        var quiet = table.Register((_, _) => Task.FromResult<PluginValue?>(null));
        var broken = table.Register((_, _) => throw new InvalidOperationException("secret path C:\\private"));
        await using var session = new PluginSandboxSession(binding, view, new Dispatcher(), new Authority(binding), table);
        await InitializeAsync(session, view);

        Assert.True(await session.ReceiveAsync(view.RootIdentity, true, view.TrustedRoot, () => Invoke("c1", quiet), Ct));
        var ok = Last(view);
        Assert.True(ok.Property("ok").BooleanValue);
        Assert.True(!ok.PropertyNames.Contains("result", StringComparer.Ordinal) || ok.Property("result").Kind == PluginValueKind.Undefined);

        Assert.True(await session.ReceiveAsync(view.RootIdentity, true, view.TrustedRoot, () => Invoke("c2", broken), Ct));
        var failed = Last(view);
        Assert.False(failed.Property("ok").BooleanValue);
        Assert.DoesNotContain("secret", failed.Property("error").StringValue, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_id_or_a_frame_without_a_table_gets_an_error_and_runs_nothing()
    {
        var view = new View();
        var binding = Binding();
        var (table, id, calls) = Table();
        await using var session = new PluginSandboxSession(binding, view, new Dispatcher(), new Authority(binding), table);
        await InitializeAsync(session, view);
        Assert.True(await session.ReceiveAsync(view.RootIdentity, true, view.TrustedRoot, () => Invoke("c1", "not-issued"), Ct));
        var response = Last(view);
        Assert.False(response.Property("ok").BooleanValue);
        Assert.Equal("callback-response", response.Property("type").StringValue, StringComparer.Ordinal);
        Assert.Empty(calls);

        // after the host clears the table (the dialog ended) the issued id is unknown too
        table.Clear();
        Assert.True(await session.ReceiveAsync(view.RootIdentity, true, view.TrustedRoot, () => Invoke("c2", id), Ct));
        Assert.False(Last(view).Property("ok").BooleanValue);
        Assert.Empty(calls);

        var other = new View();
        var otherBinding = Binding();
        await using var bare = new PluginSandboxSession(otherBinding, other, new Dispatcher(), new Authority(otherBinding));
        await InitializeAsync(bare, other);
        Assert.True(await bare.ReceiveAsync(other.RootIdentity, true, other.TrustedRoot, () => Invoke("c3", id), Ct));
        Assert.False(Last(other).Property("ok").BooleanValue);
    }

    [Fact]
    public async Task A_callback_of_another_frame_is_unknown_to_this_one()
    {
        var first = new View();
        var firstBinding = Binding();
        var (firstTable, id, firstCalls) = Table();
        await using var one = new PluginSandboxSession(firstBinding, first, new Dispatcher(), new Authority(firstBinding), firstTable);
        var second = new View();
        var secondBinding = Binding();
        var (secondTable, _, secondCalls) = Table();
        await using var two = new PluginSandboxSession(secondBinding, second, new Dispatcher(), new Authority(secondBinding), secondTable);
        await InitializeAsync(one, first);
        await InitializeAsync(two, second);

        Assert.True(await two.ReceiveAsync(second.RootIdentity, true, second.TrustedRoot, () => Invoke("c1", id), Ct));
        Assert.False(Last(second).Property("ok").BooleanValue);
        Assert.Empty(firstCalls);
        Assert.Empty(secondCalls);
    }

    [Fact]
    public async Task Callbacks_are_refused_before_the_init_is_issued_from_a_direct_frame_and_from_a_foreign_view()
    {
        var view = new View();
        var binding = Binding();
        var (table, id, calls) = Table();
        await using var session = new PluginSandboxSession(binding, view, new Dispatcher(), new Authority(binding), table);
        await InitializeAsync(session, view, issueInit: false);
        Assert.False(await session.ReceiveAsync(view.RootIdentity, true, view.TrustedRoot, () => Invoke("c1", id), Ct));
        Assert.Empty(calls);

        using var init = JsonDocument.Parse("{\"type\":\"init\",\"payload\":{\"mode\":\"slot\",\"pluginId\":\"fixture\",\"tier\":\"untrusted\",\"manifest\":{\"id\":\"fixture\",\"version\":\"1.0.0\",\"permissions\":[],\"settings\":{}},\"code\":\"\",\"locale\":\"en\",\"slot\":\"plugin-dialog\",\"extraProps\":{},\"theme\":{\"dark\":false,\"fontFamily\":\"x\",\"vars\":{}}}}");
        Assert.True(await session.IssueInitAsync(PluginValue.FromJson(init.RootElement), Ct));
        var payloadRead = false;
        Assert.False(await session.ReceiveAsync(view.RootIdentity, false, view.TrustedRoot, () => { payloadRead = true; return Invoke("c2", id); }, Ct));
        Assert.False(await session.ReceiveAsync(new object(), true, view.TrustedRoot, () => Invoke("c3", id), Ct));
        Assert.False(payloadRead);
        Assert.Empty(calls);
    }

    [Fact]
    public async Task A_grant_that_changed_since_the_frame_started_stops_the_callback_before_it_runs()
    {
        var view = new View();
        var binding = Binding();
        var authority = new Authority(binding);
        var (table, id, calls) = Table();
        await using var session = new PluginSandboxSession(binding, view, new Dispatcher(), authority, table);
        await InitializeAsync(session, view);
        authority.Current = new PluginGrantSnapshot(binding.Package, binding.GrantGeneration + 1, true, ["ui:observe"], [], binding.Tier);

        Assert.False(await session.ReceiveAsync(view.RootIdentity, true, view.TrustedRoot, () => Invoke("c1", id), Ct));
        Assert.Empty(calls);
    }

    [Fact]
    public async Task A_stopped_frame_runs_no_callback_and_a_reply_that_loses_the_grant_is_not_written()
    {
        var view = new View();
        var binding = Binding();
        var authority = new Authority(binding);
        var (table, id, calls) = Table(arguments =>
        {
            // the grant is revoked while the callback runs
            authority.Current = new PluginGrantSnapshot(binding.Package, binding.GrantGeneration, false, ["ui:observe"], [], binding.Tier);
            return Task.FromResult<PluginValue?>(null);
        });
        await using var session = new PluginSandboxSession(binding, view, new Dispatcher(), authority, table);
        await InitializeAsync(session, view);
        var before = view.Sent.Count;
        Assert.False(await session.ReceiveAsync(view.RootIdentity, true, view.TrustedRoot, () => Invoke("c1", id), Ct));
        Assert.Single(calls);
        Assert.Equal(before, view.Sent.Count);

        await session.DisposeAsync();
        Assert.False(await session.ReceiveAsync(view.RootIdentity, true, view.TrustedRoot, () => Invoke("c2", id), Ct));
        Assert.Single(calls);
    }

    [Fact]
    public void The_table_issues_unguessable_ids_and_holds_a_handful_of_callbacks()
    {
        var table = new PluginHostCallbacks();
        var ids = Enumerable.Range(0, PluginHostCallbacks.MaxCallbacks).Select(_ => table.Register((_, _) => Task.FromResult<PluginValue?>(null))).ToArray();
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ids, id => Assert.Equal(24, id.Length));
        Assert.Throws<InvalidOperationException>(() => table.Register((_, _) => Task.FromResult<PluginValue?>(null)));
        Assert.NotNull(table.Find(ids[0]));
        Assert.Null(table.Find("0"));
        table.Clear();
        Assert.Null(table.Find(ids[0]));
    }

    private sealed class Authority(PluginRuntimeBinding binding) : IPluginDeliveryAuthority
    {
        public PluginGrantSnapshot Current { get; set; } = new(binding.Package, binding.GrantGeneration, true, ["ui:observe"], [], binding.Tier);
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

    private sealed class View : IPluginSandboxWebView
    {
        public object RootIdentity { get; } = new();
        public Uri TrustedRoot { get; } = new("https://plugin.invalid/host/index.html");
        public List<byte[]> Sent { get; } = [];
        public Task NavigateAsync(Uri uri, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> PostAsync(Func<Action<string>, CancellationToken, ValueTask<bool>> deliver, CancellationToken ct) =>
            deliver(message => Sent.Add(Encoding.UTF8.GetBytes(message)), ct).AsTask();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Dispatcher : IPluginApiDispatcher
    {
        public Task<PluginCallOutcome> DispatchAsync(PluginRuntimeBinding binding, PluginApiRequest request, CancellationToken ct) =>
            Task.FromResult(PluginCallOutcome.Failed(PluginErrorCode.Unavailable));
    }
}
