// The actions against the fake JMAP server (M2 plan 6.1 exit tests): delete and undo as inserts without a reset, move rollback on a refusal, archive by month
// creating the folder, junk keywords, mark folder read, empty Trash asking first, mixed tag tri-state, a push delta applying once, an intercept hook that stops the
// write, read-only accounts sending nothing. Owner actions.

#pragma warning disable MA0002, VSTHRD003 // asserts compare strings ordinally by nature
using System.Collections.Specialized;
using System.Text.Json.Nodes;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Write.Payloads;
using Barbican.Core.Plugins.Hooks;
using Barbican.Presentation.Mail;
using Barbican.FakeJmap;

namespace Barbican.Presentation.Tests.Actions;

public sealed class MailActionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Keyword(ActionHarness h, string id, string keyword) => h.Server.Store.Email(id)!["keywords"]![keyword]?.ToJsonString() ?? string.Empty;

    /// <summary>The first row that is not starred (the newest message of the seed is).</summary>
    private static int Plain(ActionHarness h) => h.List.Messages.ToList().FindIndex(r => !r.Flagged);

    private static string[] Boxes(ActionHarness h, string id) => [.. ((JsonObject)h.Server.Store.Email(id)!["mailboxIds"]!).Select(kv => kv.Key)];

    [Fact]
    public async Task Delete_then_undo_removes_and_inserts_rows_without_a_reset()
    {
        using var h = await ActionHarness.StartAsync(prefs: """{"confirmBulkDelete": false}""");
        await h.OpenAsync("inbox");
        var before = h.List.Messages.Count();
        var targets = h.Pick(0, 1);
        var ids = targets.Select(t => t.EmailId).ToList();

        var r = await h.Actions.DeleteAsync(targets, Ct);
        Assert.True(r.IsOk);
        Assert.Equal(before - 2, h.List.Messages.Count());
        Assert.All(ids, id => Assert.Equal([h.Folder("trash")], Boxes(h, id)));
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, h.Changes);
        Assert.Contains(h.Toasts.Toasts, t => string.Equals(t.ActionLabel, "Undo", StringComparison.Ordinal));
        Assert.Single(h.Undo.History);

        Assert.True(await h.Undo.UndoLatestAsync(Ct));
        Assert.Equal(before, h.List.Messages.Count());
        Assert.All(ids, id => Assert.Equal([h.Folder("inbox")], Boxes(h, id)));
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, h.Changes);
        Assert.Empty(h.Undo.History);
    }

    [Fact]
    public async Task A_selection_that_hit_the_select_all_cap_always_asks_and_says_the_folder_may_hold_more()
    {
        // decision M2-Q5: the cap of 10 000 is stated in the confirmation, even with the bulk-delete question off
        using var h = await ActionHarness.StartAsync();
        await h.OpenAsync("inbox");
        h.Confirmer.Answer = false;
        var one = h.Pick(0)[0];
        var many = Enumerable.Range(0, MessageListViewModel.AllInFolderCap).Select(i => one with { EmailId = "e" + i, RowId = "e" + i }).ToList();
        Assert.True((await h.Actions.DeleteAsync(many, Ct)).IsOk);
        var ask = Assert.Single(h.Confirmer.Asked);
        Assert.Equal("Delete 10000 messages?", ask.Title);
        Assert.Contains("stops at 10 000 messages", ask.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_bulk_delete_asks_first_and_declining_sends_nothing()
    {
        using var h = await ActionHarness.StartAsync();
        await h.OpenAsync("inbox");
        h.Confirmer.Answer = false;
        var requests = h.Requests;
        var rows = h.List.Messages.Count();
        var r = await h.Actions.DeleteAsync(h.Pick(0, 1, 2), Ct);
        Assert.True(r.IsOk);
        Assert.Equal("Delete 3 messages?", Assert.Single(h.Confirmer.Asked).Title);
        Assert.Equal(requests, h.Requests);
        Assert.Equal(rows, h.List.Messages.Count());

        // one message needs no question
        h.Confirmer.Asked.Clear();
        Assert.True((await h.Actions.DeleteAsync(h.Pick(0), Ct)).IsOk);
        Assert.Empty(h.Confirmer.Asked);
    }

    [Fact]
    public async Task Delete_in_trash_destroys_and_has_no_undo()
    {
        using var h = await ActionHarness.StartAsync(prefs: """{"confirmBulkDelete": false}""");
        var trash = h.Folder("trash");
        var id = h.Server.Store.Deliver(new FakeMail { Subject = "old", Text = "x" }, trash);
        await h.OpenAsync("trash");
        Assert.True((await h.Actions.DeleteAsync(h.Pick(0), Ct)).IsOk);
        Assert.Null(h.Server.Store.Email(id));
        Assert.Empty(h.Undo.History);
        Assert.DoesNotContain(h.Toasts.Toasts, t => string.Equals(t.ActionLabel, "Undo", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Move_rolls_back_the_rows_the_server_refused()
    {
        using var h = await ActionHarness.StartAsync();
        await h.OpenAsync("inbox");
        var targets = h.Pick(0, 1);
        h.Server.Store.Destroy(targets[1].EmailId); // gone on the server, still in the loaded list
        var rows = h.List.Messages.Select(r => r.Id).ToList();

        var r = await h.Actions.MoveAsync(targets, h.Folder("archive"), Ct);
        Assert.True(r.IsOk);

        // the accepted one stays gone, the refused one is back where it was, and an error toast says why
        var now = h.List.Messages.Select(x => x.Id).ToList();
        Assert.DoesNotContain(targets[0].EmailId, now);
        Assert.Contains(targets[1].EmailId, now);
        Assert.Equal(rows.Count - 1, now.Count);
        Assert.Contains(h.Toasts.Toasts, t => t.IsError && t.Message.Contains("no longer exists", StringComparison.Ordinal));
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, h.Changes);
    }

    [Fact]
    public async Task A_move_into_a_folder_whose_my_rights_forbid_adding_sends_nothing()
    {
        using var h = await ActionHarness.StartAsync();
        await h.OpenAsync("inbox");
        var archive = h.Folder("archive");
        var rights = System.Text.Json.JsonDocument.Parse("""{"mayAddItems": false}""").RootElement.Clone();
        h.Sidebar.Update(new MailboxTree([.. h.Sidebar.Tree.All.Select(m => string.Equals(m.Id, archive, StringComparison.Ordinal) ? m with { Extra = new Dictionary<string, System.Text.Json.JsonElement> { ["myRights"] = rights } } : m)]));
        var rows = h.List.Messages.Select(r => r.Id).ToList();
        var before = h.Requests;

        var r = await h.Actions.MoveAsync(h.Pick(0), archive, Ct);

        Assert.False(r.IsOk);
        Assert.Equal(before, h.Requests);
        Assert.Equal(rows, h.List.Messages.Select(x => x.Id).ToList());
        Assert.Contains(h.Toasts.Toasts, t => t.IsError);
    }

    [Fact]
    public async Task A_failed_write_restores_every_row()
    {
        using var h = await ActionHarness.StartAsync();
        await h.OpenAsync("inbox");
        var rows = h.List.Messages.Select(r => r.Id).ToList();
        h.Server.Store.Destroy(rows[0]);
        var r = await h.Actions.MoveAsync(h.Pick(0), h.Folder("archive"), Ct);
        Assert.False(r.IsOk);
        Assert.Equal(rows, h.List.Messages.Select(x => x.Id));
        Assert.Contains(h.Toasts.Toasts, t => t.IsError);
        Assert.Empty(h.Undo.History);
    }

    [Fact]
    public async Task Archive_by_month_creates_the_dated_folder()
    {
        using var h = await ActionHarness.StartAsync(prefs: """{"archiveMode": "month"}""");
        await h.OpenAsync("inbox");
        var target = h.Pick(0);
        var received = h.List.Find(target[0].EmailId)!.ReceivedAt!.Value;
        var r = await h.Actions.ArchiveAsync(target, Ct);
        Assert.True(r.IsOk);
        var boxes = h.Server.Store.Mailboxes();
        var year = boxes.First(m => string.Equals((string?)m["name"], received.Year.ToString("D4", System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal) && string.Equals((string?)m["parentId"], h.Folder("archive"), StringComparison.Ordinal));
        var month = boxes.First(m => string.Equals((string?)m["name"], received.Month.ToString("D2", System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal) && string.Equals((string?)m["parentId"], (string?)year["id"], StringComparison.Ordinal));
        Assert.Equal([(string)month["id"]!], Boxes(h, target[0].EmailId));
    }

    [Fact]
    public async Task Junk_and_not_junk_set_the_keywords_and_the_folder()
    {
        using var h = await ActionHarness.StartAsync();
        await h.OpenAsync("inbox");
        var t = h.Pick(0);
        Assert.True((await h.Actions.ReportSpamAsync(t, true, Ct)).IsOk);
        Assert.Equal([h.Folder("junk")], Boxes(h, t[0].EmailId));
        Assert.Equal("true", Keyword(h, t[0].EmailId, "$junk"));

        await h.OpenAsync("junk");
        var again = h.Pick(h.List.Messages.ToList().FindIndex(r => string.Equals(r.Id, t[0].EmailId, StringComparison.Ordinal)));
        Assert.True((await h.Actions.ReportSpamAsync(again, false, Ct)).IsOk);
        Assert.Equal([h.Folder("inbox")], Boxes(h, t[0].EmailId));
        Assert.Equal("true", Keyword(h, t[0].EmailId, "$notjunk"));
        Assert.Equal(string.Empty, Keyword(h, t[0].EmailId, "$junk"));
    }

    [Fact]
    public async Task Empty_trash_asks_first()
    {
        using var h = await ActionHarness.StartAsync();
        var trash = h.Folder("trash");
        h.Server.Store.Deliver(new FakeMail { Subject = "a", Text = "x" }, trash);
        h.Server.Store.Deliver(new FakeMail { Subject = "b", Text = "x" }, trash);
        var folder = new MailboxRef("acct", string.Empty, trash, "trash");

        h.Confirmer.Answer = false;
        var requests = h.Requests;
        Assert.True((await h.Actions.EmptyFolderAsync(folder, Ct)).IsOk);
        Assert.Equal(requests, h.Requests);
        Assert.Equal(2, h.Server.Store.Query(new JsonObject { ["inMailbox"] = trash }).Count);

        h.Confirmer.Answer = true;
        Assert.True((await h.Actions.EmptyFolderAsync(folder, Ct)).IsOk);
        Assert.Empty(h.Server.Store.Query(new JsonObject { ["inMailbox"] = trash }));
        Assert.Equal("Empty Trash?", h.Confirmer.Asked[0].Title);
    }

    [Fact]
    public async Task Mark_folder_read_marks_the_whole_folder()
    {
        using var h = await ActionHarness.StartAsync();
        await h.OpenAsync("inbox");
        var inbox = h.Folder("inbox");
        Assert.NotEmpty(h.Server.Store.Query(new JsonObject { ["inMailbox"] = inbox, ["notKeyword"] = "$seen" }));
        Assert.True((await h.Actions.MarkFolderReadAsync(new MailboxRef("acct", string.Empty, inbox, "inbox"), false, Ct)).IsOk);
        Assert.Empty(h.Server.Store.Query(new JsonObject { ["inMailbox"] = inbox, ["notKeyword"] = "$seen" }));
        Assert.All(h.List.Messages, r => Assert.False(r.Unread));
    }

    [Fact]
    public async Task Mark_all_folders_read_asks_first_and_then_clears_every_unread_message()
    {
        using var h = await ActionHarness.StartAsync();
        await h.OpenAsync("inbox");
        h.Confirmer.Answer = false;
        Assert.True((await h.Actions.MarkAllFoldersReadAsync("acct", Ct)).IsOk);
        Assert.Equal("Mark all folders as read", h.Confirmer.Asked[0].Title);
        Assert.False(h.Confirmer.Asked[0].Danger);
        Assert.NotEmpty(h.Server.Store.Query(new JsonObject { ["notKeyword"] = "$seen" }));

        h.Confirmer.Answer = true;
        Assert.True((await h.Actions.MarkAllFoldersReadAsync("acct", Ct)).IsOk);
        Assert.Empty(h.Server.Store.Query(new JsonObject { ["notKeyword"] = "$seen" }));
        Assert.All(h.List.Messages, r => Assert.False(r.Unread));
    }

    [Fact]
    public async Task A_folder_cannot_be_dropped_into_its_own_child_and_nothing_is_sent()
    {
        using var h = await ActionHarness.StartAsync();
        var parent = h.Server.Store.AddMailbox("Parent", null);
        var child = h.Server.Store.AddMailbox("Child", null, parent);
        h.Sidebar.Update((await h.Active.Mail!.GetMailboxesAsync(Ct)).Value!);
        var requests = h.Requests;
        var r = await h.Actions.MoveFolderAsync("acct", parent, child, Core.DragDrop.DropZone.Inside, Ct);
        Assert.False(r.IsOk);
        Assert.Contains("inside itself", r.Error!.Message, StringComparison.Ordinal);
        Assert.Equal(requests, h.Requests);

        // a legal reorder writes
        Assert.True((await h.Actions.MoveFolderAsync("acct", child, parent, Core.DragDrop.DropZone.Before, Ct)).IsOk);
        Assert.Null(h.Server.Store.Mailboxes().First(m => string.Equals((string?)m["id"], child, StringComparison.Ordinal))["parentId"]);
    }

    [Fact]
    public async Task A_mixed_batch_shows_the_tag_tri_state_and_the_command_adds_it_to_all()
    {
        using var h = await ActionHarness.StartAsync();
        await h.OpenAsync("inbox");
        var t = h.Pick(0, 1);
        Assert.False(MailActionServiceState(t));
        Assert.True((await h.Actions.SetTagAsync(t.Take(1).ToList(), "work", true, Ct)).IsOk);
        var refreshed = h.Pick(0, 1);
        Assert.Null(Barbican.Presentation.Actions.MailActionService.TagState(refreshed, "work"));
        Assert.True((await h.Actions.SetTagAsync(refreshed, "work", true, Ct)).IsOk);
        Assert.All(t, x => Assert.Equal("true", Keyword(h, x.EmailId, "$label:work")));
        var all = h.Pick(0, 1);
        Assert.True(Barbican.Presentation.Actions.MailActionService.TagState(all, "work"));
    }

    private static bool MailActionServiceState(IReadOnlyList<MailTarget> t) => Barbican.Presentation.Actions.MailActionService.TagState(t, "work") != false;

    [Fact]
    public async Task A_push_delta_for_a_removed_message_applies_once()
    {
        using var h = await ActionHarness.StartAsync(prefs: """{"confirmBulkDelete": false}""");
        await h.OpenAsync("inbox");
        var total = h.List.Total;
        var t = h.Pick(0);
        Assert.True((await h.Actions.DeleteAsync(t, Ct)).IsOk);
        Assert.Equal(total - 1, h.List.Total);
        var rows = h.List.Messages.Select(r => r.Id).ToList();
        h.Changes.Clear();

        // the server's own delta for the same removal arrives afterwards
        h.Hub.Publish(new ListDeltaEvent(new ListDelta { Removed = [t[0].EmailId] }));
        Assert.Equal(total - 1, h.List.Total);
        Assert.Equal(rows, h.List.Messages.Select(r => r.Id));
        Assert.Empty(h.Changes);
    }

    [Fact]
    public async Task An_intercept_hook_that_returns_false_stops_the_write()
    {
        using var h = await ActionHarness.StartAsync(prefs: """{"confirmBulkDelete": false}""");
        await h.OpenAsync("inbox");
        using var _ = h.Bus.Intercept<EmailDeletePayload>(HookNames.OnBeforeEmailDelete, (_, _) => ValueTask.FromResult(false));
        var requests = h.Requests;
        var rows = h.List.Messages.Select(r => r.Id).ToList();
        Assert.True((await h.Actions.DeleteAsync(h.Pick(0, 1), Ct)).IsOk);
        Assert.Equal(requests, h.Requests);
        Assert.Equal(rows, h.List.Messages.Select(r => r.Id));
        Assert.Empty(h.Undo.History);
    }

    [Fact]
    public async Task Observer_hooks_fire_after_the_server_accepted()
    {
        using var h = await ActionHarness.StartAsync();
        await h.OpenAsync("inbox");
        var seen = new TaskCompletionSource<EmailStarPayload>();
        using var _ = h.Bus.Observe<EmailStarPayload>(HookNames.OnEmailStarToggle, (p, _) =>
        {
            seen.TrySetResult(p);
            return ValueTask.CompletedTask;
        });
        var t = h.Pick(Plain(h));
        Assert.True((await h.Actions.SetStarAsync(t, true, Ct)).IsOk);
        var payload = await seen.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        Assert.Equal([t[0].EmailId], payload.EmailIds);
        Assert.True(payload.Starred);
        Assert.Equal("true", Keyword(h, t[0].EmailId, "$flagged"));
    }

    [Fact]
    public async Task A_read_only_account_sends_nothing_and_says_so()
    {
        using var h = await ActionHarness.StartAsync(readOnly: true);
        await h.OpenAsync("inbox");
        var requests = h.Requests;
        var t = h.Pick(Plain(h));
        var r = await h.Actions.SetStarAsync(t, true, Ct);
        Assert.False(r.IsOk);
        Assert.Equal(requests, h.Requests);
        Assert.Contains(h.Toasts.Toasts, t => t.IsError && string.Equals(t.Message, "This account is read-only.", StringComparison.Ordinal));
        Assert.False(h.List.Messages.First(x => string.Equals(x.Id, t[0].EmailId, StringComparison.Ordinal)).Flagged);
    }

    [Fact]
    public async Task Star_changes_the_row_at_once_and_undo_puts_it_back()
    {
        using var h = await ActionHarness.StartAsync();
        await h.OpenAsync("inbox");
        var t = h.Pick(Plain(h));
        Assert.True((await h.Actions.SetStarAsync(t, true, Ct)).IsOk);
        Assert.True(h.List.Messages.First(r => string.Equals(r.Id, t[0].EmailId, StringComparison.Ordinal)).Flagged);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, h.Changes);
        Assert.True(await h.Undo.UndoLatestAsync(Ct));
        Assert.False(h.List.Messages.First(r => string.Equals(r.Id, t[0].EmailId, StringComparison.Ordinal)).Flagged);
        Assert.Equal(string.Empty, Keyword(h, t[0].EmailId, "$flagged"));
    }

    [Fact]
    public async Task The_undo_history_keeps_ten_entries()
    {
        using var h = await ActionHarness.StartAsync();
        await h.OpenAsync("inbox");
        for (var i = 0; i < 12; i++)
        {
            var t = h.Pick(0);
            Assert.True((await h.Actions.SetStarAsync(t, !t[0].Keywords.Flagged, Ct)).IsOk);
        }

        Assert.Equal(10, h.Undo.History.Count);
    }

    [Fact]
    public async Task Create_rename_and_delete_a_folder_fire_their_hooks()
    {
        using var h = await ActionHarness.StartAsync();
        var seen = new List<string>();
        using var a = h.Bus.Observe<MailboxCreatedPayload>(HookNames.OnMailboxCreate, (p, _) => { seen.Add("create:" + p.Name); return ValueTask.CompletedTask; });
        using var b = h.Bus.Observe<MailboxRenamedPayload>(HookNames.OnMailboxRename, (p, _) => { seen.Add("rename:" + p.PreviousName + ">" + p.Name); return ValueTask.CompletedTask; });
        using var c = h.Bus.Observe<MailboxDeletePayload>(HookNames.OnMailboxDelete, (p, _) => { seen.Add("delete:" + p.Name); return ValueTask.CompletedTask; });

        var made = await h.Actions.CreateFolderAsync("acct", "  Receipts ", null, Ct);
        Assert.True(made.IsOk);
        h.Sidebar.Update((await h.Active.Mail!.GetMailboxesAsync(Ct)).Value!);
        Assert.True((await h.Actions.RenameFolderAsync("acct", made.Value!.Id, "Bills", Ct)).IsOk);
        h.Sidebar.Update((await h.Active.Mail!.GetMailboxesAsync(Ct)).Value!);
        Assert.True((await h.Actions.DeleteFolderAsync("acct", made.Value.Id, Ct)).IsOk);
        Assert.False(h.Server.Store.HasMailbox(made.Value.Id));
        await Task.Delay(100, Ct);
        // observers run off the caller's thread: the order between them is not promised
        Assert.Equal(3, seen.Count);
        Assert.Contains("create:Receipts", seen);
        Assert.Contains("rename:Receipts>Bills", seen);
        Assert.Contains("delete:Bills", seen);
    }
}
