// Push on a virtual clock: the event stream (RFC 8620 §7.3: closeafter=no, ping=30), the 75 s silence deadline, reconnection with
// backoff and jitter, catch-up on every open, the 700 ms coalesce, and open -> push -> delta with no list reset; Rust main.rs:499
// start_push, :532 schedule_refresh, jmap.rs:1108 watch. Owner jmap.

using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Service;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Sync;
using Barbican.Core.Tests.Jmap;
using Barbican.FakeJmap;
using Microsoft.Extensions.Time.Testing;

namespace Barbican.Core.Tests.Sync;

public sealed class PushTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Running(Fake F, FakeTimeProvider Clock, MailSync Sync, RecordingHooks Hooks, Task Run, CancellationTokenSource Stop, List<MailSyncEvent> Events)
    {
        public int Idle => Hooks.Seen.Count(h => string.Equals(h.Hook, HookNames.OnSyncStateChange, StringComparison.Ordinal) && h.Payload is SyncStatePayload { State: "idle" });

        public bool Traced(string line)
        {
            lock (F.Trace)
            {
                return F.Trace.Contains(line);
            }
        }

        public int Count(string line)
        {
            lock (F.Trace)
            {
                return F.Trace.Count(l => string.Equals(l, line, StringComparison.Ordinal));
            }
        }

        public List<MailSyncEvent> Take()
        {
            while (Sync.Events.TryRead(out var e))
            {
                Events.Add(e);
            }

            return Events;
        }

        /// <summary>Lets the 700 ms coalesce fire and waits for that sync to finish.</summary>
        public async Task SettleAsync()
        {
            var idle = Idle;
            Clock.Advance(MailSync.Coalesce);
            await Poll.UntilAsync(() => Idle > idle, "the coalesced sync");
        }

        public async Task StopAsync()
        {
            await Stop.CancelAsync();

            // the engine's run loop was started by this fixture (StartAsync); awaiting it here is the shutdown
#pragma warning disable VSTHRD003
            await Run;
#pragma warning restore VSTHRD003
            Stop.Dispose();
            Sync.Dispose();
        }
    }

    private static async Task<Running> StartAsync()
    {
        var clock = Fake.VirtualClock();
        var f = await Fake.StartAsync(time: clock);
        var s = new MailService(f.Client, clock);
        var hooks = new RecordingHooks();
        var sync = new MailSync(s, hooks, clock, new PushBackoff(_ => 0));
        await s.GetMailboxesAsync(Ct);
        var q = new ListQuery { MailboxId = f.Server.Store.MailboxId("inbox"), PageSize = 50 };
        sync.Watch(q, (await s.QueryAsync(q, null, Ct)).Value!);
        var stop = new CancellationTokenSource();
        var r = new Running(f, clock, sync, hooks, sync.RunAsync(stop.Token), stop, []);
        await Poll.UntilAsync(() => r.Traced("PUSH open"), "the stream to open");
        await r.SettleAsync();
        return r;
    }

    [Fact]
    [Rfc("J-040", "RFC 8620 7")]
    [Rfc("J-023", "RFC 8620 5.6")]
    public async Task Open_push_delta_never_resets_the_list()
    {
        var r = await StartAsync();
        Assert.Contains(r.Take(), e => e is PushStateEvent { Connected: true });
        Assert.Contains(r.Events, e => e is MailboxesChangedEvent);
        var id = r.F.Server.Store.Deliver(new FakeMail { Subject = "Pushed", ReceivedAt = new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero) });
        await Poll.UntilAsync(() => r.Traced("PUSH state Email,EmailDelivery,Mailbox,Thread"), "the push");
        await r.SettleAsync();
        var events = r.Take();
        Assert.Contains(events, e => e is ListDeltaEvent d && d.Delta.Added.Contains((id, 0)));
        Assert.Contains(events, e => e is NewMailEvent n && string.Equals(n.Emails[0].Id, id, StringComparison.Ordinal));
        Assert.DoesNotContain(events, e => e is ListResetEvent);
        Assert.Contains(r.Hooks.Seen, h => string.Equals(h.Hook, HookNames.OnPushConnectionChange, StringComparison.Ordinal) && h.Payload is PushConnectionPayload { Connected: true });
        await r.StopAsync();
    }

    [Fact]
    [Rfc("J-022", "RFC 8621 2.2")]
    public async Task A_burst_of_pushes_is_one_sync()
    {
        var r = await StartAsync();
        var before = r.Count("Mailbox/changes -> Mailbox/changes");
        var ids = r.F.Server.Store.EmailIds();
        r.F.Server.Store.SetKeyword(ids[0], "$flagged", true);
        r.Clock.Advance(TimeSpan.FromMilliseconds(300));
        r.F.Server.Store.SetKeyword(ids[1], "$flagged", true);
        await Poll.UntilAsync(() => r.Count("PUSH state Email,Mailbox") == 2, "two pushes");
        await r.SettleAsync();
        Assert.Equal(before + 1, r.Count("Mailbox/changes -> Mailbox/changes"));
        await r.StopAsync();
    }

    [Fact]
    [Rfc("J-038", "RFC 8620 7.3")]
    [Rfc("J-039", "RFC 8620 7.3")]
    public async Task A_silent_stream_is_dropped_after_75_s_and_reopened_after_the_backoff()
    {
        var r = await StartAsync();
        r.Clock.Advance(TimeSpan.FromSeconds(60));
        r.F.Server.Push.Ping();
        await Poll.UntilAsync(() => r.Traced("PUSH ping"), "the ping");
        await Task.Delay(50, Ct);
        r.Clock.Advance(TimeSpan.FromSeconds(74));
        await Task.Delay(50, Ct);
        Assert.False(r.Traced("PUSH silent"));
        r.Clock.Advance(TimeSpan.FromSeconds(1));
        await Poll.UntilAsync(() => r.Take().Exists(e => e is PushStateEvent { Connected: false }), "the drop");
        Assert.True(r.Traced("PUSH silent"));
        await Task.Delay(50, Ct);
        r.Clock.Advance(TimeSpan.FromSeconds(1.9));
        await Task.Delay(50, Ct);
        Assert.Equal(1, r.F.Server.Push.Opened);
        r.Clock.Advance(TimeSpan.FromSeconds(0.1));
        await Poll.UntilAsync(() => r.F.Server.Push.Opened == 2, "the reopen after 2 s");
        await r.StopAsync();
    }

    [Fact]
    [Rfc("J-040", "RFC 8620 7")]
    public async Task Mail_that_arrived_while_the_stream_was_down_is_caught_up_on_reopen()
    {
        var r = await StartAsync();
        r.F.Server.Push.CloseAll();
        await Poll.UntilAsync(() => r.Take().Exists(e => e is PushStateEvent { Connected: false }), "the drop");
        Assert.True(r.Traced("PUSH closed"));
        await Task.Delay(50, Ct);
        var id = r.F.Server.Store.Deliver(new FakeMail { Subject = "While down", ReceivedAt = new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero) });
        r.Take();
        r.Events.Clear();
        r.Clock.Advance(TimeSpan.FromSeconds(2));
        await Poll.UntilAsync(() => r.Count("PUSH open") == 2, "the reopen");
        await r.SettleAsync();
        Assert.Contains(r.Take(), e => e is ListDeltaEvent d && d.Delta.Added.Contains((id, 0)));
        Assert.DoesNotContain(r.Events, e => e is ListResetEvent);
        await r.StopAsync();
    }

    [Fact]
    [Rfc("J-041", "RFC 8620 7.1")]
    public async Task A_state_change_for_another_account_syncs_nothing()
    {
        var r = await StartAsync();
        var lines = r.F.Trace.Count;
        r.F.Server.Push.Send("state", """{"@type":"StateChange","changed":{"other":{"Email":"sz"}}}""");
        r.F.Server.Push.Send("calendarAlert", "{}");
        await Poll.UntilAsync(() => r.Traced("PUSH state "), "the foreign state change");
        r.Clock.Advance(TimeSpan.FromSeconds(1));
        await Task.Delay(50, Ct);
        Assert.Equal(lines + 1, r.F.Trace.Count);
        await r.StopAsync();
    }

    [Fact]
    [Rfc("J-039", "RFC 8620 7.3")]
    public void Backoff_doubles_from_2_s_to_5_min_with_jitter_and_resets_after_a_healthy_stream()
    {
        var b = new PushBackoff(n => n - 1);
        var waits = Enumerable.Range(0, 10).Select(_ => b.Next(TimeSpan.FromSeconds(1)).TotalMilliseconds).ToList();
        Assert.Equal([2999, 5999, 11999, 23999, 47999, 95999, 191999, 383999, 449999, 449999], waits);
        Assert.Equal(PushBackoff.Max, b.Seconds);
        Assert.Equal(2000, b.Next(TimeSpan.FromMinutes(3)).TotalMilliseconds - 999);
        var zero = new PushBackoff(_ => -5);
        Assert.Equal(2000, zero.Next(TimeSpan.Zero).TotalMilliseconds);
        var random = PushBackoff.WithRandomJitter();
        Assert.InRange(random.Next(TimeSpan.Zero).TotalMilliseconds, 2000, 2999);
    }
}
