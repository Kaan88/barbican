// New tests for Fable review 1 F-14 / F-15 in the list: removals scoped to their query, a delta reaching the list once, a reset that keeps the
// rows and the selection or closes the reader, the unread-first Inbox as two queries, a sync problem on screen, events of another account.
// Rust mail.rs:599 fetch_unread_first, :759 sync. Owner mailcore.

#pragma warning disable MA0002, VSTHRD003 // asserts compare strings ordinally by nature; fakes hand back TaskCompletionSource tasks on purpose
using Barbican.Core.Common;
using Barbican.Core.Mail.Cache;
using Barbican.Core.Mail.Model;
using Barbican.Presentation.Accounts;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Tests.Support;

namespace Barbican.Presentation.Tests.Mail;

public sealed class ListFixTests
{
    private static Result<ListPage> Ok(ListPage p) => Result<ListPage>.Ok(p);

    private static async Task<ListHarness> OpenedAsync(string prefs = "{}", int count = 10, int total = 100, bool more = true, MailboxRole role = MailboxRole.Inbox)
    {
        var h = new ListHarness(prefs);
        h.Service.OnQuery = (_, after, _) => Task.FromResult(Ok(after is null ? Mails.Page(0, count, total, more) : Mails.Page(count, count, total, more)));
        await h.List.OpenFolderAsync("inbox", role, "Inbox");
        return h;
    }

    [Fact]
    public void The_removed_ids_of_a_changed_event_are_not_the_lists_removals()
    {
        // F-14: after a folder switch the old query's removed ids may name a message moved INTO the list now shown
        var list = new LoadedList();
        list.Replace(Mails.Page(0, 5, 5, more: false));
        var touched = list.Apply(new EmailsChangedEvent(new Dictionary<string, EmailSummary>(), ["m2"], []));
        Assert.False(touched);
        Assert.Equal(5, list.Total);
        Assert.Contains(list.Emails, e => string.Equals(e.Id, "m2", StringComparison.Ordinal));
    }

    [Fact]
    public void A_delta_is_applied_once_and_its_changed_event_finds_nothing_left_to_do()
    {
        var list = new LoadedList();
        list.Replace(Mails.Page(0, 5, 5, more: false));
        var fresh = Mails.Make("new", unread: true, hoursAgo: 0.1);
        var fresher = new Dictionary<string, EmailSummary> { ["new"] = fresh };
        list.Apply(new ListDelta { Added = [("new", 0)], Emails = fresher, Removed = ["m4"], Total = 5 });
        Assert.Equal(["new", "m0", "m1", "m2", "m3"], list.Emails.Select(e => e.Id));
        Assert.False(list.Apply(new EmailsChangedEvent(fresher, ["m4"], [])));
        Assert.Equal(5, list.Total);
    }

    [Fact]
    public void The_list_uses_the_core_delta_rules_so_destroyed_threads_are_forgotten()
    {
        var list = new LoadedList();
        list.Replace(new ListPage { Items = [Mails.Make("a") with { ThreadId = "t1", ThreadCount = 3 }], Total = 1, Threads = new Dictionary<string, int> { ["t1"] = 3 } });
        list.Apply(new ListDelta { Removed = ["a"], DestroyedThreads = ["t1"], Total = 0 });
        Assert.Empty(list.Emails);
        Assert.Equal(0, list.Total);
    }

    [Fact]
    public async Task A_reset_reloads_as_many_rows_as_were_loaded_and_keeps_the_selection()
    {
        using var h = await OpenedAsync("""{"emailsPerPage": 10}""", count: 10, more: true);
        await h.List.LoadMoreAsync();
        Assert.Equal(20, h.List.Messages.Count());
        h.List.Select(h.List.Messages.ElementAt(15), OpenReason.Click, ListDirection.None);
        h.Service.Queries.Clear();
        h.Service.OnQuery = (q, _, _) => Task.FromResult(Ok(Mails.Page(0, q.PageSize, 100, more: true)));
        h.Hub.Publish(new ListResetEvent());
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal(20, h.Service.Queries[0].Query.PageSize);
        Assert.Equal(20, h.List.Messages.Count());
        Assert.Equal("m15", h.List.Selected?.Id);
        Assert.Equal(0, h.Opener.Closed);
    }

    [Fact]
    public async Task A_reset_that_loses_the_open_message_closes_the_reader()
    {
        using var h = await OpenedAsync(count: 10, more: false, total: 10);
        h.List.Select(h.List.Messages.ElementAt(3), OpenReason.Click, ListDirection.None);
        h.Service.OnQuery = (_, _, _) => Task.FromResult(Ok(new ListPage { Items = [Mails.Make("other")], Total = 1 }));
        await h.List.ReloadAsync();
        Assert.Null(h.List.Selected);
        Assert.Equal(1, h.Opener.Closed);
    }

    [Fact]
    public async Task The_unread_first_inbox_is_two_queries_and_pages_through_the_read_ones()
    {
        using var h = new ListHarness("""{"interfaceLayout": "mountain-view", "mvUnreadSectionSize": 5}""");
        h.Service.OnQuery = (q, after, _) =>
        {
            if (string.Equals(q.NotKeyword, "$seen", StringComparison.Ordinal))
            {
                return Task.FromResult(Ok(new ListPage { Items = [Mails.Make("u1", unread: true, hoursAgo: 2), Mails.Make("u2", unread: true, hoursAgo: 4)], Total = 2 }));
            }

            return Task.FromResult(Ok(after is null
                ? new ListPage { Items = [Mails.Make("r1", hoursAgo: 1), Mails.Make("r2", hoursAgo: 3)], Total = 4, Next = new ListCursor("r2") }
                : new ListPage { Items = [Mails.Make("r3", hoursAgo: 5), Mails.Make("r4", hoursAgo: 6)], Total = 4 }));
        };
        await h.List.OpenFolderAsync("inbox", MailboxRole.Inbox, "Inbox");
        Assert.Equal(5, h.Service.Queries[0].Query.PageSize);
        Assert.Equal("$seen", h.Service.Queries[0].Query.NotKeyword);
        Assert.Equal("$seen", h.Service.Queries[1].Query.HasKeyword);
        Assert.Equal(["u1", "u2", "r1", "r2"], h.List.Messages.Select(m => m.Id));
        Assert.Equal(6, h.List.Total);
        Assert.Empty(h.Sync.Loaded);
        Assert.Single(h.Sync.Watched);
        await h.List.LoadMoreAsync();
        Assert.Equal("$seen", h.Service.Queries[^1].Query.HasKeyword);
        Assert.Equal(["u1", "u2", "r1", "r2", "r3", "r4"], h.List.Messages.Select(m => m.Id));
    }

    [Fact]
    public async Task A_sync_problem_is_shown_and_cleared_when_the_stream_is_back()
    {
        using var h = await OpenedAsync();
        h.Hub.Publish(new SyncProblemEvent("Sign in again", NeedsSignIn: true));
        Assert.Equal("Sign in again", h.List.Error);
        h.Hub.Publish(new PushStateEvent(true));
        Assert.Null(h.List.Error);
    }

    [Fact]
    public async Task An_event_of_the_account_that_was_active_before_a_switch_is_ignored()
    {
        using var h = await OpenedAsync();
        var before = h.List.Messages.Count();
        var delta = new ListDeltaEvent(new ListDelta { Removed = ["m1"], Total = 99 });
        h.Hub.Publish(delta, "stale");
        Assert.Equal(before, h.List.Messages.Count());
        h.Hub.Publish(delta, "acct");
        Assert.Equal(before - 1, h.List.Messages.Count());
    }

    [Fact]
    public async Task The_body_cache_is_patched_under_the_account_that_produced_the_event_not_the_one_active_now()
    {
        var cache = new BodyCache();
        var body = new EmailBody { Id = "m1" };
        cache.Put("old", "m1", body);
        cache.Put("now", "m1", body);
        var hub = new MailEventHub(new FakeActiveMail { AccountKey = "now" }, new Barbican.Presentation.Common.InlineDispatcher());
        using var sync = new BodyCacheSync(hub, cache);
        await sync.StartAsync(TestContext.Current.CancellationToken);
        hub.Publish(new EmailsChangedEvent(new Dictionary<string, EmailSummary>(), ["m1"], []), "old");
        Assert.False(cache.Contains("old", "m1"));
        Assert.True(cache.Contains("now", "m1"));
    }
}
