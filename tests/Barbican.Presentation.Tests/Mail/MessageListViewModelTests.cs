// New tests (no Rust original): the list against fake IMailService / IMailSync. Spec B-SYNC-036/037, B-LST-014/016, I-119..I-156. Owner mailcore.

#pragma warning disable MA0002, VSTHRD003 // asserts compare strings ordinally by nature; fakes hand back TaskCompletionSource tasks on purpose
using System.Collections.Specialized;
using Barbican.Core.Common;
using Barbican.Core.Mail.Model;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;
using Barbican.Presentation.Common;
using Barbican.Presentation.Input;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Tests.Support;

namespace Barbican.Presentation.Tests.Mail;

public sealed class MessageListViewModelTests
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
    public async Task Opening_a_folder_shows_the_first_page_under_a_date_header()
    {
        using var h = await OpenedAsync();
        Assert.Equal(11, h.List.Rows.Count);
        Assert.Equal("Today", Assert.IsType<GroupHeaderViewModel>(h.List.Rows[0]).Title);
        Assert.Equal(100, h.List.Total);
        Assert.True(h.List.CanLoadMore);
        Assert.False(h.List.IsLoading);
        Assert.Equal("inbox", Assert.Single(h.Sync.Watched).MailboxId);
        // the engine gets the page with its states, so the next push is a delta and not a reset (Rust mail.rs:34 states_of)
        Assert.Single(h.Sync.Loaded);
        Assert.True(h.List.LastSync.Reset);
        Assert.Empty(h.Opener.Opened);
    }

    [Fact]
    public async Task Load_more_anchors_on_the_last_message_and_appends_without_a_reset()
    {
        using var h = await OpenedAsync();
        h.Changes.Clear();
        await h.List.LoadMoreAsync();
        Assert.Equal(new ListCursor("m9"), h.Service.Queries[^1].After);
        Assert.Equal(21, h.List.Rows.Count);
        Assert.Equal(10, h.List.LastSync.Inserted);
        Assert.False(h.List.LastSync.Reset);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, h.Changes);
    }

    [Fact]
    public async Task A_pushed_message_is_one_insert_and_never_a_reset()
    {
        using var h = await OpenedAsync();
        h.Changes.Clear();
        var fresh = Mails.Make("new", unread: true, hoursAgo: 0.1);
        h.Hub.Publish(new ListDeltaEvent(new ListDelta { Added = [("new", 0)], Emails = new Dictionary<string, EmailSummary> { ["new"] = fresh }, Total = 101 }));
        Assert.Equal(new SyncStats(Inserted: 1), h.List.LastSync);
        Assert.Equal([NotifyCollectionChangedAction.Add], h.Changes);
        Assert.Equal("new", Assert.IsType<MessageRowViewModel>(h.List.Rows[1]).Id);
        Assert.Equal(101, h.List.Total);
    }

    [Fact]
    public async Task A_keyword_change_sets_one_row_in_place()
    {
        using var h = await OpenedAsync();
        h.Changes.Clear();
        var unread = Mails.Make("m2", unread: true, hoursAgo: 1);
        h.Hub.Publish(new EmailsChangedEvent(new Dictionary<string, EmailSummary> { ["m2"] = unread }, [], []));
        Assert.Equal(1, h.List.LastSync.Set);
        Assert.Equal([NotifyCollectionChangedAction.Replace], h.Changes);
        Assert.True(h.List.Messages.First(m => string.Equals(m.Id, "m2", StringComparison.Ordinal)).Unread);
    }

    [Fact]
    public async Task A_refused_delta_reloads_and_keeps_the_old_rows_until_the_page_is_ready()
    {
        using var h = await OpenedAsync();
        var gate = new TaskCompletionSource<Result<ListPage>>();
        h.Service.OnQuery = (_, _, _) => gate.Task;
        h.Hub.Publish(new ListResetEvent());
        Assert.True(h.List.IsLoading);
        Assert.Equal(11, h.List.Rows.Count);
        gate.SetResult(Ok(Mails.Page(0, 10, 100, more: true)));
        Assert.False(h.List.IsLoading);
        Assert.True(h.List.LastSync.Untouched, h.List.LastSync.ToString());
    }

    [Fact]
    public async Task Switching_folder_cancels_the_previous_load_and_drops_its_answer()
    {
        using var h = new ListHarness();
        var slow = new TaskCompletionSource<Result<ListPage>>();
        var slowToken = CancellationToken.None;
        h.Service.OnQuery = (q, _, ct) =>
        {
            if (string.Equals(q.MailboxId, "a", StringComparison.Ordinal))
            {
                slowToken = ct;
                return slow.Task;
            }

            return Task.FromResult(Ok(Mails.Page(100, 3, 3, more: false)));
        };
        var first = h.List.OpenFolderAsync("a", MailboxRole.None, "A");
        await h.List.OpenFolderAsync("b", MailboxRole.None, "B");
        Assert.True(slowToken.IsCancellationRequested);
        slow.SetResult(Ok(Mails.Page(0, 5, 5, more: false)));
        await first;
        Assert.Equal("m100", h.List.Messages.First().Id);
        Assert.Equal(3, h.List.Messages.Count());
        Assert.Equal("b", h.List.MailboxId);
        Assert.Equal("b", h.Sync.Watched[^1].MailboxId);
    }

    [Fact]
    public async Task The_old_rows_stay_while_the_next_folder_loads()
    {
        using var h = await OpenedAsync();
        var gate = new TaskCompletionSource<Result<ListPage>>();
        h.Service.OnQuery = (_, _, _) => gate.Task;
        var opening = h.List.OpenFolderAsync("sent", MailboxRole.Sent, "Sent");
        Assert.True(h.List.IsLoading);
        Assert.Equal(11, h.List.Rows.Count);
        gate.SetResult(Ok(Mails.Page(50, 2, 2, more: false)));
        await opening;
        Assert.Equal(3, h.List.Rows.Count);
        Assert.False(h.List.IsLoading);
    }

    [Fact]
    public async Task A_delta_that_arrives_while_the_next_folder_loads_does_not_touch_the_rows()
    {
        using var h = await OpenedAsync();
        var gate = new TaskCompletionSource<Result<ListPage>>();
        h.Service.OnQuery = (_, _, _) => gate.Task;
        var opening = h.List.OpenFolderAsync("sent", MailboxRole.Sent, "Sent");
        h.Changes.Clear();
        var other = Mails.Make("from-the-old-folder", unread: true, hoursAgo: 0.01);
        h.Hub.Publish(new ListDeltaEvent(new ListDelta { Added = [("from-the-old-folder", 0)], Emails = new Dictionary<string, EmailSummary> { ["from-the-old-folder"] = other }, Total = 11 }));
        Assert.Empty(h.Changes);
        gate.SetResult(Ok(Mails.Page(50, 2, 2, more: false)));
        await opening;
        Assert.DoesNotContain(h.List.Messages, r => string.Equals(r.Id, "from-the-old-folder", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_delta_or_reset_of_another_watched_list_is_ignored()
    {
        using var h = await OpenedAsync();
        h.Changes.Clear();
        var queries = h.Service.Queries.Count;
        var elsewhere = new ListQuery { MailboxId = "archive" };
        var other = Mails.Make("from-another-list", unread: true, hoursAgo: 0.01);
        h.Hub.Publish(new ListDeltaEvent(new ListDelta { Added = [("from-another-list", 0)], Emails = new Dictionary<string, EmailSummary> { ["from-another-list"] = other }, Total = 11 }) { Query = elsewhere });
        h.Hub.Publish(new ListResetEvent { Query = elsewhere });
        Assert.Empty(h.Changes);
        Assert.Equal(queries, h.Service.Queries.Count);
        Assert.DoesNotContain(h.List.Messages, r => string.Equals(r.Id, "from-another-list", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_failed_load_keeps_the_rows_and_reports_why()
    {
        using var h = await OpenedAsync();
        h.Service.OnQuery = (_, _, _) => Task.FromResult(Result<ListPage>.Fail(new Core.Jmap.Errors.TransportError("offline", true)));
        await h.List.ReloadAsync();
        Assert.Equal("offline", h.List.Error);
        Assert.Equal(11, h.List.Rows.Count);
    }

    [Fact]
    public async Task J_and_k_move_the_selection_and_open_the_message()
    {
        using var h = await OpenedAsync();
        Assert.True(h.Keys.Handle(new KeyChord("j"), KeySource.Keyboard));
        Assert.Equal("m0", h.List.Selected?.Id);
        Assert.True(h.Keys.Handle(new KeyChord("ArrowDown"), KeySource.WebView));
        Assert.Equal("m1", h.List.Selected?.Id);
        Assert.True(h.Keys.Handle(new KeyChord("k"), KeySource.Keyboard));
        Assert.Equal("m0", h.List.Selected?.Id);
        Assert.Equal(["m0", "m1", "m0"], h.Opener.Opened.Select(o => o.Id));
        Assert.All(h.Opener.Opened, o => Assert.Equal(OpenReason.Key, o.Reason));
        Assert.Equal(ListDirection.Up, h.Opener.Opened[2].Direction);
    }

    [Fact]
    public async Task J_across_a_page_boundary_waits_for_the_next_page_then_moves()
    {
        using var h = await OpenedAsync(count: 4, total: 8, more: true);
        h.List.Select(h.List.Messages.Last(), OpenReason.Click, ListDirection.None);
        var gate = new TaskCompletionSource<Result<ListPage>>();
        h.Service.OnQuery = (_, _, _) => gate.Task;
        Assert.True(h.Keys.Handle(new KeyChord("j"), KeySource.Keyboard));
        Assert.Equal("m3", h.List.Selected?.Id);
        gate.SetResult(Ok(Mails.Page(4, 4, 8, more: false)));
        Assert.Equal("m4", h.List.Selected?.Id);
        Assert.Equal(8, h.List.Messages.Count());
        Assert.False(h.List.CanLoadMore);
    }

    [Fact]
    public async Task Moving_near_the_end_loads_the_next_page_ahead_of_time()
    {
        using var h = await OpenedAsync(count: 10, total: 30, more: true);
        for (var i = 0; i < 8; i++)
        {
            h.Keys.Handle(new KeyChord("j"), KeySource.Keyboard);
        }

        Assert.Equal(20, h.List.Messages.Count());
    }

    [Fact]
    public async Task Rapid_is_on_while_keys_repeat_and_off_after_the_calm()
    {
        using var h = await OpenedAsync();
        h.Keys.Handle(new KeyChord("j"), KeySource.Keyboard);
        h.Time.Advance(TimeSpan.FromMilliseconds(33));
        h.Keys.Handle(new KeyChord("j"), KeySource.Keyboard);
        Assert.True(h.Motion.Rapid);
        h.Time.Advance(TimeSpan.FromMilliseconds(100));
        Assert.True(h.Motion.Rapid);
        h.Time.Advance(TimeSpan.FromMilliseconds(100));
        Assert.False(h.Motion.Rapid);
        h.Keys.Handle(new KeyChord("j"), KeySource.Keyboard);
        Assert.False(h.Motion.Rapid);
    }

    [Fact]
    public async Task Search_runs_the_text_over_the_open_folder_and_empty_text_goes_back()
    {
        using var h = await OpenedAsync();
        h.Service.OnQuery = (q, _, _) => Task.FromResult(Ok(q.SearchText is null ? Mails.Page(0, 10, 100, true) : Mails.Page(70, 2, 2, false)));
        await h.List.SearchAsync("invoice");
        Assert.Equal("invoice", h.Service.Queries[^1].Query.SearchText);
        Assert.Equal("inbox", h.Service.Queries[^1].Query.MailboxId);
        Assert.Equal(2, h.List.Messages.Count());
        Assert.Equal(2, h.List.Total);
        await h.List.SearchAsync("  ");
        Assert.Null(h.Service.Queries[^1].Query.SearchText);
        Assert.Equal(10, h.List.Messages.Count());
    }

    [Fact]
    public async Task The_search_hook_can_change_the_query_and_the_results()
    {
        using var h = await OpenedAsync();
        h.Bus.Transform<SearchPayload>(HookNames.OnSearch, (p, _) => ValueTask.FromResult(p with { Query = p.Query + " from:ada" }));
        h.Bus.Transform<SearchResultsPayload>(HookNames.OnSearchResults, (p, _) => ValueTask.FromResult(p with { EmailIds = [.. p.EmailIds.Reverse()] }));
        h.Service.OnQuery = (_, _, _) => Task.FromResult(Ok(Mails.Page(0, 3, 3, false)));
        await h.List.SearchAsync("x");
        Assert.Equal("x from:ada", h.Service.Queries[^1].Query.SearchText);
        Assert.Equal(["m2", "m1", "m0"], h.List.Messages.Select(m => m.Id));
    }

    [Fact]
    public async Task The_unread_first_inbox_puts_unread_messages_under_their_own_header()
    {
        using var h = new ListHarness("""{"interfaceLayout": "mountain-view", "mvUnreadSectionSize": 5}""");
        h.Service.OnQuery = (_, _, _) => Task.FromResult(Ok(new ListPage
        {
            Items = [Mails.Make("r1", hoursAgo: 1), Mails.Make("u1", unread: true, hoursAgo: 2), Mails.Make("r2", hoursAgo: 3), Mails.Make("u2", unread: true, hoursAgo: 4)],
            Total = 4,
        }));
        await h.List.OpenFolderAsync("inbox", MailboxRole.Inbox, "Inbox");
        Assert.Equal(
            ["Unread", "u1", "u2", "Everything else", "r1", "r2"],
            h.List.Rows.Select(r => r is GroupHeaderViewModel g ? g.Title : ((MessageRowViewModel)r).Id));
    }

    [Fact]
    public async Task Other_folders_keep_the_date_order_even_in_the_unread_first_layout()
    {
        using var h = new ListHarness("""{"interfaceLayout": "mountain-view"}""");
        h.Service.OnQuery = (_, _, _) => Task.FromResult(Ok(Mails.Page(0, 3, 3, false)));
        await h.List.OpenFolderAsync("sent", MailboxRole.Sent, "Sent");
        Assert.Equal("Today", Assert.IsType<GroupHeaderViewModel>(h.List.Rows[0]).Title);
    }

    [Fact]
    public async Task Selection_survives_a_refresh_and_clears_when_the_message_is_gone()
    {
        using var h = await OpenedAsync();
        h.List.Select(h.List.Messages.ElementAt(2), OpenReason.Click, ListDirection.None);
        h.Hub.Publish(new ListDeltaEvent(new ListDelta { Added = [("new", 0)], Emails = new Dictionary<string, EmailSummary> { ["new"] = Mails.Make("new") } }));
        Assert.Equal("m2", h.List.Selected?.Id);
        h.Hub.Publish(new ListDeltaEvent(new ListDelta { Removed = ["m2"], Total = 99 }));
        Assert.Null(h.List.Selected);
    }

    [Fact]
    public async Task Escape_clears_a_search_first_and_then_closes_the_reader()
    {
        using var h = await OpenedAsync();
        h.List.Select(h.List.Messages.First(), OpenReason.Click, ListDirection.None);
        await h.List.SearchAsync("x");
        Assert.True(h.Keys.Handle(new KeyChord("Escape"), KeySource.Keyboard));
        Assert.Equal(string.Empty, h.List.SearchText);
        Assert.Equal(0, h.Opener.Closed);
        Assert.True(h.Keys.Handle(new KeyChord("Escape"), KeySource.Keyboard));
        Assert.Equal(1, h.Opener.Closed);
        Assert.Null(h.List.Selected);
    }

    [Fact]
    public async Task Switching_account_empties_the_list_and_closes_the_reader()
    {
        using var h = await OpenedAsync();
        h.Active.RaiseChanged();
        Assert.Empty(h.List.Rows);
        Assert.Equal(0, h.List.Total);
        Assert.Equal(1, h.Opener.Closed);
    }

    [Fact]
    public async Task Hooks_hear_about_the_fetched_page_the_selection_and_the_folder()
    {
        using var h = new ListHarness();
        var fetched = 0;
        var selected = new TaskCompletionSource<SelectionChangedPayload>();
        var folder = new TaskCompletionSource<MailboxChangedPayload>();
        h.Bus.Transform<EmailsFetchedPayload>(HookNames.OnEmailsFetched, (p, _) =>
        {
            fetched++;
            return ValueTask.FromResult(p with { Emails = [.. p.Emails.Take(2)] });
        });
        h.Bus.Observe<SelectionChangedPayload>(HookNames.OnEmailSelectionChange, (p, _) =>
        {
            selected.TrySetResult(p);
            return ValueTask.CompletedTask;
        });
        h.Bus.Observe<MailboxChangedPayload>(HookNames.OnMailboxChange, (p, _) =>
        {
            folder.TrySetResult(p);
            return ValueTask.CompletedTask;
        });
        h.Service.OnQuery = (_, _, _) => Task.FromResult(Ok(Mails.Page(0, 5, 5, false)));
        await h.List.OpenFolderAsync("inbox", MailboxRole.Inbox, "Inbox");
        Assert.Equal(1, fetched);
        Assert.Equal(2, h.List.Messages.Count());
        h.List.Select(h.List.Messages.First(), OpenReason.Click, ListDirection.None);
        Assert.Equal(["m0"], (await selected.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)).EmailIds);
        Assert.Equal("inbox", (await folder.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)).Role);
    }

    [Fact]
    public async Task Plugins_can_add_badges_to_rows_without_resetting_the_list()
    {
        using var h = await OpenedAsync();
        h.Changes.Clear();
        h.Bus.Transform<ListItemRenderPayload>(HookNames.OnEmailListItemRender, (p, _) => ValueTask.FromResult(string.Equals(p.Email.Id, "m1", StringComparison.Ordinal) ? p with { Badges = ["VIP"] } : p));
        h.Hub.Publish(new EmailsChangedEvent(new Dictionary<string, EmailSummary> { ["m5"] = Mails.Make("m5", unread: true, hoursAgo: 2.5) }, [], []));
        Assert.Equal(["VIP"], h.List.Messages.First(m => string.Equals(m.Id, "m1", StringComparison.Ordinal)).Badges);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, h.Changes);
    }
}
