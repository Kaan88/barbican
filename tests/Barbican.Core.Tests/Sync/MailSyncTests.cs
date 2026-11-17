// The sync engine against the fake server: Mailbox/changes (RFC 8621 §2.2), the 7-call list delta (RFC 8620 §5.2, §5.6) and its
// full-refetch rules (cannotCalculateChanges, tooManyChanges, hasMore, a Stalwart state going back), the new-mail check and the hooks;
// Rust jmap.rs:440, :592, main.rs:532, :560. Owner jmap.

using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Service;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Sync;
using Barbican.Core.Tests.Jmap;
using Barbican.FakeJmap;

namespace Barbican.Core.Tests.Sync;

public sealed class MailSyncTests
{
    private static readonly HashSet<string> Mail = new(["Email", "Thread", "Mailbox", "EmailDelivery"], StringComparer.Ordinal);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(Fake F, MailService S, MailSync Sync, RecordingHooks Hooks, ListQuery Q)> WatchInboxAsync(FakeJmapOptions? options = null)
    {
        var f = await Fake.StartAsync(options);
        var s = new MailService(f.Client, f.Time);
        var hooks = new RecordingHooks();
        var sync = new MailSync(s, hooks, f.Time);
        await s.GetMailboxesAsync(Ct);
        var q = new ListQuery { MailboxId = f.Server.Store.MailboxId("inbox"), PageSize = 50 };
        sync.Watch(q, (await s.QueryAsync(q, null, Ct)).Value!);
        await sync.SyncNowAsync(new HashSet<string>(["EmailDelivery"], StringComparer.Ordinal), Ct);
        Drain(sync);
        return (f, s, sync, hooks, q);
    }

    private static List<MailSyncEvent> Drain(MailSync sync)
    {
        var events = new List<MailSyncEvent>();
        while (sync.Events.TryRead(out var e))
        {
            events.Add(e);
        }

        return events;
    }

    [Fact]
    [Rfc("J-023", "RFC 8620 5.6")]
    [Rfc("J-049", "RFC 8621 3")]
    public async Task New_mail_arrives_as_a_delta_at_the_top()
    {
        var (f, _, sync, hooks, q) = await WatchInboxAsync();
        var first = f.Server.Store.Query(new System.Text.Json.Nodes.JsonObject { ["inMailbox"] = q.MailboxId })[^1];
        var id = f.Server.Store.Deliver(new FakeMail { Subject = "Hello", ReceivedAt = new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero), ReplyTo = first });
        f.Trace.Clear();
        await sync.SyncNowAsync(Mail, Ct);
        var events = Drain(sync);
        var delta = Assert.Single(events.OfType<ListDeltaEvent>()).Delta;
        Assert.Equal((id, 0), Assert.Single(delta.Added));
        Assert.Equal(6, delta.Threads[delta.Emails[id].ThreadId!]);
        Assert.DoesNotContain(events, e => e is ListResetEvent);
        Assert.Equal("Hello", Assert.Single(events.OfType<NewMailEvent>()).Emails[0].Subject);
        Assert.Contains(hooks.Seen, h => string.Equals(h.Hook, HookNames.OnNewEmailReceived, StringComparison.Ordinal) && string.Equals(((EmailSummary)h.Payload!).Id, id, StringComparison.Ordinal));
        Assert.Contains(f.Trace, l => l.StartsWith("Email/changes,Email/queryChanges,Email/get,Email/get,Thread/changes,Thread/get,Thread/get ->", StringComparison.Ordinal));
        Assert.Contains(f.Trace, l => l.StartsWith("Mailbox/changes ->", StringComparison.Ordinal));
    }

    [Fact]
    [Rfc("J-022", "RFC 8621 2.2")]
    [Rfc("J-021", "RFC 8620 5.1")]
    public async Task A_read_flag_fetches_only_the_folder_counts()
    {
        var (f, s, sync, _, _) = await WatchInboxAsync();
        var unread = f.Server.Store.Emails(f.Server.Store.EmailIds()).First(e => e["keywords"]!["$seen"] is null && e["mailboxIds"]![f.Server.Store.MailboxId("inbox")!] is not null);
        f.Server.Store.SetKeyword((string)unread["id"]!, "$seen", true);
        f.Trace.Clear();
        await sync.SyncNowAsync(new HashSet<string>(["Mailbox"], StringComparer.Ordinal), Ct);
        var tree = Assert.Single(Drain(sync).OfType<MailboxesChangedEvent>()).Tree;
        Assert.Equal(f.Server.Store.State("Mailbox"), tree.State);
        var inbox = tree.All.Single(m => m.Role == MailboxRole.Inbox);
        Assert.Equal((int)f.Server.Store.Mailboxes().Single(m => string.Equals((string?)m["id"], inbox.Id, StringComparison.Ordinal))["unreadEmails"]!, inbox.UnreadEmails);
        Assert.Equal("Inbox", inbox.Name);
        Assert.Equal(["Mailbox/changes -> Mailbox/changes", "Mailbox/get -> Mailbox/get"], f.Trace);
        Assert.Equal(s.LastMailboxes!.Value.State, tree.State);
    }

    [Fact]
    [Rfc("J-022", "RFC 8621 2.2")]
    public async Task A_renamed_or_new_folder_is_fetched_whole()
    {
        var (f, _, sync, _, _) = await WatchInboxAsync();
        var projects = f.Server.Store.Mailboxes().Single(m => string.Equals((string?)m["name"], "Projects", StringComparison.Ordinal))["id"]!.GetValue<string>();
        f.Server.Store.RenameMailbox(projects, "Plans");
        f.Server.Store.AddMailbox("Receipts", null);
        await sync.SyncNowAsync(new HashSet<string>(["Mailbox"], StringComparer.Ordinal), Ct);
        var tree = Assert.Single(Drain(sync).OfType<MailboxesChangedEvent>()).Tree;
        Assert.Contains(tree.All, m => string.Equals(m.Name, "Plans", StringComparison.Ordinal));
        Assert.Contains(tree.All, m => string.Equals(m.Name, "Receipts", StringComparison.Ordinal));
        Assert.Equal(8, tree.All.Count);
    }

    [Theory]
    [Rfc("J-025", "RFC 8620 5.6")]
    [Rfc("J-026", "Stalwart state codec")]
    [InlineData(FakeFault.CannotCalculateChanges)]
    [InlineData(FakeFault.TooManyChanges)]
    [InlineData(FakeFault.QueryStateWentBack)]
    [InlineData(FakeFault.EmailChangesFail)]
    public async Task No_delta_resets_the_list(FakeFault fault)
    {
        var (f, _, sync, _, q) = await WatchInboxAsync();
        f.Server.Store.Deliver(new FakeMail { Subject = "x", ReceivedAt = new DateTimeOffset(2026, 11, 21, 0, 0, 0, TimeSpan.Zero) });
        f.Server.Inject(fault);
        await sync.SyncNowAsync(new HashSet<string>(["Email"], StringComparer.Ordinal), Ct);
        var events = Drain(sync);
        Assert.Equal(q, Assert.Single(events.OfType<ListResetEvent>()).Query);
        Assert.DoesNotContain(events, e => e is ListDeltaEvent);
    }

    [Fact]
    [Rfc("J-025", "RFC 8620 5.2")]
    public async Task More_changes_than_maxChanges_reset_the_list()
    {
        var (f, _, sync, _, _) = await WatchInboxAsync(new FakeJmapOptions { MaxObjectsInGet = 2 });
        for (var i = 0; i < 3; i++)
        {
            f.Server.Store.Deliver(new FakeMail { Subject = "m" + i, ReceivedAt = new DateTimeOffset(2026, 11, 22, i, 0, 0, TimeSpan.Zero) });
        }

        await sync.SyncNowAsync(new HashSet<string>(["Email"], StringComparer.Ordinal), Ct);
        Assert.Single(Drain(sync).OfType<ListResetEvent>());
    }

    [Fact]
    [Rfc("J-024", "RFC 8620 5.5")]
    public async Task A_page_without_canCalculateChanges_keeps_no_states()
    {
        Assert.Null(ListStates.Of(new ListPage { QueryState = "q", EmailState = "e", ThreadState = "t", CanCalculateChanges = false }));
        Assert.NotNull(ListStates.Of(new ListPage { QueryState = "q", EmailState = "e", ThreadState = "t", CanCalculateChanges = true }));
        var (f, s, sync, _, q) = await WatchInboxAsync();
        sync.Watch(q);
        await sync.SyncNowAsync(new HashSet<string>(["Email"], StringComparer.Ordinal), Ct);
        Assert.Single(Drain(sync).OfType<ListResetEvent>());
        _ = (f, s);
    }

    [Fact]
    [Rfc("J-027", "RFC 8620 5.6")]
    [Rfc("J-028", "RFC 8620 5.6")]
    public void The_delta_request_is_seven_linked_calls_and_queryChanges_has_no_maxChanges()
    {
        var (request, calls) = ListDeltaFetcher.Build("b", new System.Text.Json.Nodes.JsonObject { ["inMailbox"] = "m1" }, new ListStates("q", "e", "t"), 256);
        Assert.Equal(7, request.MethodCalls.Count);
        Assert.Equal(["Email/changes", "Email/queryChanges", "Email/get", "Email/get", "Thread/changes", "Thread/get", "Thread/get"], calls.Select(c => c.Method), StringComparer.Ordinal);
        Assert.Null(request.MethodCalls[1][1]!["maxChanges"]);
        // upToId is optional and not used: the whole delta is applied (Rust jmap.rs:592)
        Assert.Null(request.MethodCalls[1][1]!["upToId"]);
        Assert.Equal(256, (int)request.MethodCalls[0][1]!["maxChanges"]!);
        Assert.Equal("/added/*/id", (string?)request.MethodCalls[2][1]!["#ids"]!["path"]);
    }

    [Fact]
    public async Task Sync_state_hooks_wrap_each_run_and_report_errors()
    {
        var (f, _, sync, hooks, _) = await WatchInboxAsync();
        f.Server.Inject(FakeFault.Problem);
        f.Server.Inject(FakeFault.Problem);
        await sync.SyncNowAsync(new HashSet<string>(["Mailbox"], StringComparer.Ordinal), Ct);
        var states = hooks.Seen.Where(h => string.Equals(h.Hook, HookNames.OnSyncStateChange, StringComparison.Ordinal)).Select(h => ((SyncStatePayload)h.Payload!).State).ToList();
        Assert.Equal(["syncing", "idle", "syncing", "error"], states);
    }

    [Fact]
    public async Task The_new_mail_check_seeds_first_and_skips_junk()
    {
        var f = await Fake.StartAsync();
        var watcher = new NewMailWatcher(new MailService(f.Client, f.Time));
        Assert.Empty((await watcher.CheckAsync(Ct)).Value!);
        f.Server.Store.Deliver(new FakeMail { Subject = "spam", ReceivedAt = new DateTimeOffset(2026, 11, 23, 0, 0, 0, TimeSpan.Zero) }, f.Server.Store.MailboxId("junk"));
        Assert.Empty((await watcher.CheckAsync(Ct)).Value!);
        f.Server.Store.Deliver(new FakeMail { Subject = "real", ReceivedAt = new DateTimeOffset(2026, 11, 23, 1, 0, 0, TimeSpan.Zero) }, f.Server.Store.MailboxId("archive"));
        Assert.Equal("real", Assert.Single((await watcher.CheckAsync(Ct)).Value!).Subject);
        watcher.InboxOnly = true;
        f.Server.Store.Deliver(new FakeMail { Subject = "archived", ReceivedAt = new DateTimeOffset(2026, 11, 23, 2, 0, 0, TimeSpan.Zero) }, f.Server.Store.MailboxId("archive"));
        Assert.Empty((await watcher.CheckAsync(Ct)).Value!);
    }
}
