// R3-07: "is this row in Trash" for a unified row is judged by the row's own account folder tree, never by the active account's. A row of a second login
// whose Trash id differs (or whose id means another role there) must not be destroyed on a role read from the wrong account.

#pragma warning disable MA0002, VSTHRD003 // asserts compare strings ordinally by nature
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Unified;
using Barbican.Core.Mail.Write;
using Barbican.Presentation.Actions;

namespace Barbican.Presentation.Tests.Actions;

public sealed class UnifiedTrashRoleTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class OtherAccountWriters(IMailWriter writer, IReadOnlyList<(string Id, MailboxRole Role)>? folders) : IMailWriterSource
    {
        public IMailWriter? WriterOf(string accountKey) => writer;

        public bool CanWrite(string accountKey) => true;

        public Task<IReadOnlyList<(string Id, MailboxRole Role)>?> FoldersOfAsync(MailTarget target, CancellationToken ct) => Task.FromResult(folders);
    }

    private static async Task<int> ForeverQuestionsAsync(IReadOnlyList<(string Id, MailboxRole Role)>? ownFolders, Func<ActionHarness, string> mailboxOfRow)
    {
        using var h = await ActionHarness.StartAsync(prefs: """{"confirmBulkDelete": false}""");
        h.Confirmer.Answer = false;
        var actions = new MailActionService(new OtherAccountWriters(h.Writers.WriterOf("acct")!, ownFolders), h.Sidebar, h.List, h.Bus, h.Undo, h.Toasts, h.Prefs, h.Confirmer, h.Active, h.Transfer, h.Time);
        var row = UnifiedRows.RowId("other@example.org", null, "m1");
        var target = new MailTarget("other@example.org", string.Empty, row, string.Empty, row, new Dictionary<string, bool>(StringComparer.Ordinal) { [mailboxOfRow(h)] = true }, new Keywords(new Dictionary<string, bool>(StringComparer.Ordinal)));
        await actions.DeleteAsync([target], Ct);
        return h.Confirmer.Asked.Count(a => a.Title.Contains("forever", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_row_in_its_own_accounts_Trash_is_deleted_forever_even_when_that_id_is_unknown_to_the_active_account()
    {
        var own = new List<(string, MailboxRole)> { ("t9", MailboxRole.Trash), ("i9", MailboxRole.Inbox) };
        Assert.Equal(1, await ForeverQuestionsAsync(own, _ => "t9"));
    }

    [Fact]
    public async Task A_row_whose_id_is_the_active_accounts_Trash_but_another_role_in_its_own_account_is_not_destroyed()
    {
        // the active account's Trash id, which is the other account's Inbox: the active tree would say Trash
        var own = new List<(string, MailboxRole)> { ("t9", MailboxRole.Trash) };
        var asked = await ForeverQuestionsAsync(own, h => h.Folder("trash"));
        Assert.Equal(0, asked);
    }

    [Fact]
    public async Task A_row_whose_account_folders_are_unknown_is_never_destroyed()
    {
        Assert.Equal(0, await ForeverQuestionsAsync(null, h => h.Folder("trash")));
    }
}
