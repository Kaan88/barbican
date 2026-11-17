// New tests for Fable review 1 F-14 (list paging by the ids the query returned, thread order, no split of a "#tag" search) and F-15 (a
// stuck or throwing hook never stops a sync, the debounce is capped, a push auth failure and a server without push are reported or polled).
// Rust main.rs:532 schedule_refresh (700 ms, no cap), jmap.rs:524 list_page. Owner jmap.

#pragma warning disable MA0002 // asserts compare strings ordinally by nature
using System.Text.Json.Nodes;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Service;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Sync;
using Barbican.Core.Tests.Jmap;
using Barbican.FakeJmap;

namespace Barbican.Core.Tests.Sync;

public sealed class SyncFixTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Payload(int N);

    private sealed class ThrowingHooks : IHookBus
    {
        public bool HasHandlers(string hook) => true;

        public ValueTask NotifyAsync<TPayload>(string hook, TPayload payload, CancellationToken ct) => throw new InvalidCastException("a plugin bug");

        public ValueTask<bool> InterceptAsync<TPayload>(string hook, TPayload payload, CancellationToken ct) => ValueTask.FromResult(true);

        public ValueTask<T> TransformAsync<T>(string hook, T value, CancellationToken ct) => ValueTask.FromResult(value);
    }

    private static EmailSummary Row(string id) => ModelReader.Summary(JsonNode.Parse("{\"id\": \"" + id + "\"}"))!;

    private static int Idle(RecordingHooks hooks) =>
        hooks.Seen.Count(h => string.Equals(h.Hook, HookNames.OnSyncStateChange, StringComparison.Ordinal) && h.Payload is SyncStatePayload { State: "idle" });

    private static bool Traced(Fake f, string line)
    {
        lock (f.Trace)
        {
            return f.Trace.Contains(line);
        }
    }

    [Fact]
    public void More_pages_follow_the_ids_the_query_returned_not_the_rows_that_parsed()
    {
        // F-14: one message that fails to parse used to make rows.Count < limit and end paging for good
        var q = JsonNode.Parse("""{"position": 0, "total": 10, "ids": ["a", "b", "c"]}""")!.AsObject();
        var page = ListPageFetcher.Page(q, [Row("a"), Row("c")], ["a", "b", "c"], new Dictionary<string, int>(), "e", "t", 3);
        Assert.Equal(["b"], page.DroppedIds);
        Assert.Equal(new ListCursor("c") { Position = 3 }, page.Next);
        var last = ListPageFetcher.Page(JsonNode.Parse("""{"position": 7, "total": 10, "ids": ["x", "y", "z"]}""")!.AsObject(), [Row("x"), Row("z")], ["x", "y", "z"], new Dictionary<string, int>(), "e", "t", 3);
        Assert.Null(last.Next);
    }

    [Fact]
    public async Task A_conversation_comes_back_in_the_order_of_the_threads_email_ids()
    {
        var f = await Fake.StartAsync();
        var s = new MailService(f.Client, f.Time);
        var inbox = f.Server.Store.MailboxId("inbox");
        var first = f.Server.Store.Query(new JsonObject { ["inMailbox"] = inbox })[^1];
        f.Server.Store.Deliver(new FakeMail { Subject = "Re", ReceivedAt = new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero), ReplyTo = first });
        var listed = await s.QueryAsync(new ListQuery { MailboxId = inbox, PageSize = 50 }, null, Ct);
        var threadId = listed.Value!.Items.First(e => e.ThreadCount > 1).ThreadId!;
        var expected = f.Server.Store.Thread(threadId)!["emailIds"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();
        var thread = await s.GetThreadAsync(threadId, Ct);
        Assert.True(thread.IsOk);
        Assert.Equal(expected, thread.Value!.Emails.Select(e => e.Id));
    }

    [Fact]
    public async Task A_handler_that_never_returns_is_abandoned_and_the_next_one_still_runs()
    {
        var clock = Fake.VirtualClock();
        var bus = new HookBus(clock, TimeSpan.FromSeconds(5));
        var failures = new List<HookFailedEventArgs>();
        bus.HandlerFailed += (_, e) => failures.Add(e);
        var never = new TaskCompletionSource();
        var second = false;
        using var a = bus.Observe<Payload>(HookNames.OnSearch, async (_, t) => await never.Task.WaitAsync(t));
        using var b = bus.Observe<Payload>(HookNames.OnSearch, (_, _) =>
        {
            second = true;
            return ValueTask.CompletedTask;
        });
        var notifying = bus.NotifyAsync(HookNames.OnSearch, new Payload(1), Ct).AsTask();
        await Task.Delay(20, Ct);
        clock.Advance(TimeSpan.FromSeconds(5));
        await notifying.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        Assert.True(second);
        Assert.IsType<TimeoutException>(Assert.Single(failures).Error);

        // an interceptor that never answers allows, a transform that never answers leaves the value
        using var c = bus.Intercept<Payload>(HookNames.OnBeforeShortcut, async (_, t) =>
        {
            await never.Task.WaitAsync(t);
            return false;
        });
        using var d = bus.Transform<Payload>(HookNames.OnEmailsFetched, async (p, t) =>
        {
            await never.Task.WaitAsync(t);
            return p with { N = 99 };
        });
        var allowed = bus.InterceptAsync(HookNames.OnBeforeShortcut, new Payload(1), Ct).AsTask();
        var shaped = bus.TransformAsync(HookNames.OnEmailsFetched, new Payload(1), Ct).AsTask();
        await Task.Delay(20, Ct);
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.True(await allowed.WaitAsync(TimeSpan.FromSeconds(10), Ct));
        Assert.Equal(1, (await shaped.WaitAsync(TimeSpan.FromSeconds(10), Ct)).N);
    }

    [Fact]
    public async Task A_hook_that_throws_never_faults_a_sync()
    {
        var f = await Fake.StartAsync();
        var s = new MailService(f.Client, f.Time);
        using var sync = new MailSync(s, new ThrowingHooks(), f.Time);
        await s.GetMailboxesAsync(Ct);
        var q = new ListQuery { MailboxId = f.Server.Store.MailboxId("inbox"), PageSize = 50 };
        sync.Watch(q, (await s.QueryAsync(q, null, Ct)).Value!);
        await sync.SyncNowAsync(new HashSet<string>(["Email", "Mailbox"], StringComparer.Ordinal), Ct);
        await sync.DrainAsync();
        Assert.True(sync.Events.TryRead(out _));
    }

    [Fact]
    public async Task A_steady_stream_of_pushes_cannot_postpone_the_sync_beyond_three_seconds()
    {
        var clock = Fake.VirtualClock();
        var f = await Fake.StartAsync(time: clock);
        var s = new MailService(f.Client, clock);
        var hooks = new RecordingHooks();
        using var sync = new MailSync(s, hooks, clock);
        string[] types = ["Mailbox"];
        for (var i = 0; i < 5; i++)
        {
            sync.Schedule(types);
            clock.Advance(TimeSpan.FromMilliseconds(500));
        }

        Assert.Equal(0, Idle(hooks));
        sync.Schedule(types);
        clock.Advance(TimeSpan.FromMilliseconds(500));
        // 3 s since the first push: the 700 ms wait is cut short by the cap
        await Poll.UntilAsync(() => Idle(hooks) == 1, "the capped sync");
        await sync.DrainAsync();
    }

    [Fact]
    public async Task A_push_that_the_server_refuses_reaches_the_ui_once()
    {
        var clock = Fake.VirtualClock();
        var f = await Fake.StartAsync(time: clock);
        var s = new MailService(f.Client, clock);
        using var sync = new MailSync(s, new RecordingHooks(), clock, new PushBackoff(_ => 0));
        for (var i = 0; i < 4; i++)
        {
            f.Server.Inject(FakeFault.Unauthorized);
        }

        using var stop = new CancellationTokenSource();
        var run = sync.RunAsync(stop.Token);
        var problem = await sync.Events.ReadAsync(Ct).AsTask().WaitAsync(TimeSpan.FromSeconds(10), Ct);
        Assert.IsType<SyncProblemEvent>(problem);
        await stop.CancelAsync();
        await run;
    }

    [Fact]
    public async Task A_server_without_push_is_polled()
    {
        var clock = Fake.VirtualClock();
        var f = await Fake.StartAsync(new FakeJmapOptions { Push = false }, clock);
        var s = new MailService(f.Client, clock);
        var hooks = new RecordingHooks();
        using var sync = new MailSync(s, hooks, clock, new PushBackoff(_ => 0));
        await s.GetMailboxesAsync(Ct);
        using var stop = new CancellationTokenSource();
        var run = sync.RunAsync(stop.Token);
        await Poll.UntilAsync(() => Traced(f, "PUSH polling"), "the polling to start");
        clock.Advance(MailSync.Coalesce);
        await Poll.UntilAsync(() => Idle(hooks) >= 1, "the first poll");
        var before = Idle(hooks);
        clock.Advance(MailSync.PollInterval);
        await Task.Delay(20, Ct);
        clock.Advance(MailSync.Coalesce);
        await Poll.UntilAsync(() => Idle(hooks) > before, "the next poll");
        await stop.CancelAsync();
        await run;
    }
}
