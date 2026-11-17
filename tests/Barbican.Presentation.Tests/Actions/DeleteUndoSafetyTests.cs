// M2 hotfix 2 (the user's "deleted, pressed z, the message is gone: not in Trash, not anywhere"). No sequence of delete, undo, delete again, move,
// junk and archive may leave a message in no mailbox or destroy one the user only wanted in Trash. FakeJmap's Email/set is strict like Stalwart
// (crates/jmap/src/email/set.rs:978: "Message has to belong to at least one mailbox"; unknown mailbox ids are refused). Owner actions.

#pragma warning disable MA0002, VSTHRD003 // asserts compare strings ordinally by nature
using System.Text.Json.Nodes;
using Barbican.Core.Mail.Model;
using Barbican.FakeJmap;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Mail;

namespace Barbican.Presentation.Tests.Actions;

public sealed class DeleteUndoSafetyTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string[] Boxes(ActionHarness h, string id) => [.. ((JsonObject)h.Server.Store.Email(id)!["mailboxIds"]!).Select(kv => kv.Key)];

    /// <summary>Every message still exists and sits in at least one mailbox that exists.</summary>
    private static void NothingLost(ActionHarness h, IReadOnlyCollection<string> ids)
    {
        var boxes = h.Server.Store.Mailboxes().Select(m => (string)m["id"]!).ToHashSet(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            Assert.NotNull(h.Server.Store.Email(id));
            var inside = Boxes(h, id);
            Assert.NotEmpty(inside);
            Assert.All(inside, b => Assert.Contains(b, boxes));
        }
    }

    private static MailTarget TargetOf(ActionHarness h, string id)
    {
        var row = h.List.Messages.First(r => string.Equals(r.Id, id, StringComparison.Ordinal));
        return h.List.TargetOf(row);
    }

    [Fact]
    public async Task Delete_undo_delete_again_undo_again_never_destroys_or_orphans()
    {
        using var h = await ActionHarness.StartAsync(prefs: """{"confirmBulkDelete": false}""");
        await h.OpenAsync("inbox");
        var all = h.Server.Store.EmailIds();
        var id = h.Pick(0)[0].EmailId;
        for (var round = 0; round < 3; round++)
        {
            Assert.True((await h.Actions.DeleteAsync([TargetOf(h, id)], Ct)).IsOk);
            Assert.Equal([h.Folder("trash")], Boxes(h, id));
            NothingLost(h, all);
            Assert.True(await h.Undo.UndoLatestAsync(Ct));
            Assert.Equal([h.Folder("inbox")], Boxes(h, id));
            NothingLost(h, all);
        }

        Assert.Empty(h.Confirmer.Asked);
    }

    [Fact]
    public async Task A_stale_row_that_says_Trash_while_the_Inbox_is_on_screen_goes_to_Trash_and_is_not_destroyed()
    {
        // the race behind the data loss: a push copy taken while the message sat in Trash lands on the row after Undo put it back
        using var h = await ActionHarness.StartAsync(prefs: """{"confirmBulkDelete": false}""");
        await h.OpenAsync("inbox");
        var id = h.Pick(0)[0].EmailId;
        var stale = h.List.Find(id)! with { MailboxIds = new Dictionary<string, bool>(StringComparer.Ordinal) { [h.Folder("trash")] = true } };
        h.Hub.Publish(new EmailsChangedEvent(new Dictionary<string, EmailSummary>(StringComparer.Ordinal) { [id] = stale }, [], []));
        Assert.True(h.List.Find(id)!.MailboxIds.ContainsKey(h.Folder("trash")));

        Assert.True((await h.Actions.DeleteAsync([TargetOf(h, id)], Ct)).IsOk);
        Assert.NotNull(h.Server.Store.Email(id));
        Assert.Equal([h.Folder("trash")], Boxes(h, id));
        Assert.Single(h.Undo.History);
        Assert.Empty(h.Confirmer.Asked);
    }

    [Fact]
    public async Task Undo_of_a_message_whose_folders_were_unknown_puts_it_back_into_the_folder_it_left()
    {
        using var h = await ActionHarness.StartAsync(prefs: """{"confirmBulkDelete": false}""");
        await h.OpenAsync("inbox");
        var id = h.Pick(0)[0].EmailId;
        var unknown = h.Pick(0)[0] with { MailboxIds = new Dictionary<string, bool>(StringComparer.Ordinal) };
        Assert.True((await h.Actions.DeleteAsync([unknown], Ct)).IsOk);
        Assert.Equal([h.Folder("trash")], Boxes(h, id));
        Assert.True(await h.Undo.UndoLatestAsync(Ct));
        Assert.Equal([h.Folder("inbox")], Boxes(h, id));
    }

    [Fact]
    public void The_undo_set_is_never_empty_when_a_fallback_is_known()
    {
        var none = new Dictionary<string, bool>(StringComparer.Ordinal);
        Assert.Equal(["inbox"], MailActionService.UndoSet(none, "inbox").Keys);
        Assert.Empty(MailActionService.UndoSet(none, null));
        var falsy = new Dictionary<string, bool>(StringComparer.Ordinal) { ["a"] = false };
        Assert.Equal(["src"], MailActionService.UndoSet(falsy, "src").Keys);
        var had = new Dictionary<string, bool>(StringComparer.Ordinal) { ["a"] = true, ["b"] = true };
        Assert.Equal(["a", "b"], MailActionService.UndoSet(had, "src").Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Move_junk_archive_and_their_undos_keep_every_message_in_a_folder()
    {
        using var h = await ActionHarness.StartAsync(prefs: """{"confirmBulkDelete": false, "archiveMode": "month"}""");
        await h.OpenAsync("inbox");
        var all = h.Server.Store.EmailIds();
        var id = h.Pick(0)[0].EmailId;

        Assert.True((await h.Actions.MoveAsync([TargetOf(h, id)], h.Folder("archive"), Ct)).IsOk);
        NothingLost(h, all);
        Assert.True(await h.Undo.UndoLatestAsync(Ct));
        Assert.Equal([h.Folder("inbox")], Boxes(h, id));

        Assert.True((await h.Actions.ReportSpamAsync([TargetOf(h, id)], true, Ct)).IsOk);
        Assert.Equal([h.Folder("junk")], Boxes(h, id));
        Assert.True(await h.Undo.UndoLatestAsync(Ct));
        Assert.Equal([h.Folder("inbox")], Boxes(h, id));
        NothingLost(h, all);

        Assert.True((await h.Actions.ArchiveAsync([TargetOf(h, id)], Ct)).IsOk);
        Assert.NotEqual([h.Folder("inbox")], Boxes(h, id));
        NothingLost(h, all);
        Assert.True(await h.Undo.UndoLatestAsync(Ct));
        Assert.Equal([h.Folder("inbox")], Boxes(h, id));

        // undo after a move: delete the moved message, undo both
        Assert.True((await h.Actions.MoveAsync([TargetOf(h, id)], h.Folder("archive"), Ct)).IsOk);
        await h.OpenAsync("archive");
        Assert.True((await h.Actions.DeleteAsync([TargetOf(h, id)], Ct)).IsOk);
        Assert.Equal([h.Folder("trash")], Boxes(h, id));
        Assert.True(await h.Undo.UndoLatestAsync(Ct));
        Assert.Equal([h.Folder("archive")], Boxes(h, id));
        Assert.True(await h.Undo.UndoLatestAsync(Ct));
        Assert.Equal([h.Folder("inbox")], Boxes(h, id));
        NothingLost(h, all);
        Assert.Equal(all.Count, h.Server.Store.EmailIds().Count);
    }

    [Fact]
    public async Task Delete_in_Trash_always_asks_and_declining_destroys_nothing()
    {
        using var h = await ActionHarness.StartAsync(prefs: """{"confirmBulkDelete": false}""");
        var id = h.Server.Store.Deliver(new FakeMail { Subject = "old", Text = "x" }, h.Folder("trash"));
        await h.OpenAsync("trash");
        h.Confirmer.Answer = false;
        var requests = h.Requests;
        Assert.True((await h.Actions.DeleteAsync([TargetOf(h, id)], Ct)).IsOk);
        Assert.Equal("Delete this message forever?", Assert.Single(h.Confirmer.Asked).Title);
        Assert.Equal(requests, h.Requests);
        Assert.NotNull(h.Server.Store.Email(id));
    }

    [Fact]
    public async Task A_message_only_in_Trash_seen_in_a_tag_list_is_destroyed_only_after_asking()
    {
        using var h = await ActionHarness.StartAsync(prefs: """{"confirmBulkDelete": false}""");
        await h.OpenAsync("inbox");
        var id = h.Pick(0)[0].EmailId;
        var inTrash = h.Pick(0)[0] with { MailboxIds = new Dictionary<string, bool>(StringComparer.Ordinal) { [h.Folder("trash")] = true } };
        h.Server.Store.Move(id, h.Folder("trash"));
        await h.List.OpenTagAsync("red", "Red", []);
        h.Confirmer.Answer = false;
        Assert.True((await h.Actions.DeleteAsync([inTrash], Ct)).IsOk);
        Assert.Single(h.Confirmer.Asked);
        Assert.NotNull(h.Server.Store.Email(id));
    }

    [Fact]
    public async Task Undo_after_switching_folder_does_not_put_the_row_into_the_folder_on_screen()
    {
        using var h = await ActionHarness.StartAsync(prefs: """{"confirmBulkDelete": false}""");
        await h.OpenAsync("inbox");
        var id = h.Pick(0)[0].EmailId;
        Assert.True((await h.Actions.DeleteAsync([TargetOf(h, id)], Ct)).IsOk);
        await h.OpenAsync("archive");
        Assert.True(await h.Undo.UndoLatestAsync(Ct));
        Assert.DoesNotContain(h.List.Messages, r => string.Equals(r.Id, id, StringComparison.Ordinal));
        Assert.Equal([h.Folder("inbox")], Boxes(h, id));
    }

    [Fact]
    public async Task Undo_of_a_message_destroyed_meanwhile_says_so_and_sends_no_empty_set()
    {
        using var h = await ActionHarness.StartAsync(prefs: """{"confirmBulkDelete": false}""");
        await h.OpenAsync("inbox");
        var id = h.Pick(0)[0].EmailId;
        Assert.True((await h.Actions.DeleteAsync([TargetOf(h, id)], Ct)).IsOk);
        h.Server.Store.Destroy(id);
        Assert.True(await h.Undo.UndoLatestAsync(Ct));
        Assert.Contains(h.Toasts.Toasts, t => t.IsError && t.Message.Contains("no longer exists", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Undo_opens_again_the_message_that_was_open()
    {
        // Bulwark components/mail/mail-app.tsx:4083 reopenAfterUndo; before, nothing was selected after Delete then Undo
        using var h = await ActionHarness.StartAsync(prefs: """{"confirmBulkDelete": false}""");
        await h.OpenAsync("inbox");
        var row = h.List.Messages.First();
        h.List.Select(row, OpenReason.Click, ListDirection.Down);
        Assert.True((await h.Actions.DeleteAsync(h.List.Current, Ct)).IsOk);
        Assert.Null(h.List.Selected);
        Assert.True(await h.Undo.UndoLatestAsync(Ct));
        Assert.Equal(row.Id, h.List.Selected?.Id);
    }

    [Fact]
    public async Task The_fake_server_refuses_an_empty_folder_set_and_an_unknown_folder_like_Stalwart()
    {
        using var h = await ActionHarness.StartAsync();
        var id = h.Server.Store.EmailIds()[0];
        var before = Boxes(h, id);
        var writer = h.Writers.WriterOf("acct")!;
        var empty = await writer.SetMailboxIdsAsync(new Dictionary<string, IReadOnlyDictionary<string, bool>>(StringComparer.Ordinal) { [id] = new Dictionary<string, bool>(StringComparer.Ordinal) }, Ct);
        Assert.Equal("invalidProperties", Assert.Single(empty.Value!.Refused).Type);
        var unknown = await writer.MoveAsync([id], null, "no-such-folder", Ct);
        Assert.Equal("invalidProperties", Assert.Single(unknown.Value!.Refused).Type);
        var last = await writer.RemoveFromMailboxAsync([id], before[0], Ct);
        Assert.Equal("invalidProperties", Assert.Single(last.Value!.Refused).Type);
        Assert.Equal(before, Boxes(h, id));
    }
}
