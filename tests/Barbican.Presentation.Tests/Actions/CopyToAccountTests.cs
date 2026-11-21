// The copy-to-account action and its picker (spec G-048): the picker offers the other signed-in accounts' folders only (Bulwark lib/move-menu.ts:115-118,
// components/email/email-context-menu.tsx:371-379), a pick copies into that folder and leaves the list and the originals alone, and copying into
// the account the messages are already in is refused (stores/email-store.ts:5753-5760).

using Barbican.Core.Mail.Model;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Dialogs;
using Barbican.Presentation.Menus;
using Barbican.Presentation.Tests.Menus;
using static Barbican.Presentation.Tests.Menus.MenuKit;

namespace Barbican.Presentation.Tests.Actions;

public sealed class CopyToAccountTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static MailboxTree Tree() => new(
    [
        new Mailbox { Id = "i", Name = "Inbox", Role = MailboxRole.Inbox },
        new Mailbox { Id = "d", Name = "Drafts", Role = MailboxRole.Drafts },
        new Mailbox { Id = "s", Name = "Sent", Role = MailboxRole.Sent },
        new Mailbox { Id = "a", Name = "Archive", Role = MailboxRole.Archive },
        new Mailbox { Id = "r", Name = "Receipts" },
        new Mailbox { Id = "p", Name = "Projects" },
    ]);

    [Fact]
    public void The_menu_shows_Copy_to_account_only_when_another_account_is_signed_in()
    {
        var data = new FakeMenuData();
        Assert.DoesNotContain("Copy to account", Labels(MailMenus.Main(Context(MenuIds.Mail, targets: Target("m1")), data)), StringComparer.Ordinal);
        data.HasOtherAccounts = true;
        var rows = MailMenus.Main(Context(MenuIds.Mail, targets: Target("m1")), data);
        Assert.Equal(CommandIds.CopyToAccount, rows.Single(r => string.Equals(r.Label, "Copy to account", StringComparison.Ordinal)).CommandId);
    }

    [Fact]
    public void The_picker_offers_each_accounts_folders_but_no_Drafts_or_Sent()
    {
        var offered = CopyTargetAccounts.Build([("b", "b@example.org", Tree())]);

        var account = Assert.Single(offered);
        Assert.Equal(("b", "b@example.org"), (account.AccountKey, account.Label));
        Assert.Equal(["Inbox", "Archive", "Projects", "Receipts"], account.Folders.Select(f => f.Name), StringComparer.Ordinal);
        Assert.Equal("r", account.Folders[^1].MailboxId);
    }

    [Fact]
    public async Task A_pick_copies_into_the_other_account_and_leaves_the_list_alone()
    {
        using var h = await ActionHarness.StartAsync();
        await h.OpenAsync("inbox");
        var targets = h.Pick(0);
        var emailsBefore = h.Server.Store.EmailIds().Count;
        var rowsBefore = h.List.Messages.Count();
        h.Transfer.Offered = CopyTargetAccounts.Build([("b", "b@example.org", Tree())]);
        var dialogs = new PickFolders(new CopyToAccountAnswer("b", "p"));
        var registry = new CommandRegistry(ActionCommandSet.Build(h.Actions, h.List, h.Writers, h.Sidebar, h.Undo, dialogs));
        Assert.True(registry.TryGet(CommandIds.CopyToAccount, out var command));

        var r = await command.ExecuteAsync(new CommandContext(CommandSurface.ContextMenu, targets, null, null, "acct"), Ct);

        Assert.True(r.IsOk, r.Error?.Message);
        Assert.NotNull(dialogs.Asked);
        Assert.Equal(["b@example.org"], dialogs.Asked.Accounts.Select(a => a.Label), StringComparer.Ordinal);
        var copy = Assert.Single(h.Transfer.Copies);
        Assert.Equal(("b", "p"), (copy.AccountKey, copy.MailboxId));
        Assert.Equal(targets[0].EmailId, copy.Targets[0].EmailId);
        Assert.Equal("Message copied to Projects", h.Toasts.LastShown!.Message);
        Assert.Equal(emailsBefore, h.Server.Store.EmailIds().Count);
        Assert.Equal(rowsBefore, h.List.Messages.Count());
        Assert.Empty(h.Changes);
        Assert.Equal("acct", h.Transfer.AskedSource);
    }

    [Fact]
    public async Task Copying_into_the_messages_own_account_is_refused_and_a_failure_is_toasted()
    {
        using var h = await ActionHarness.StartAsync();
        await h.OpenAsync("inbox");
        var targets = h.Pick(0);

        var same = await h.Actions.CopyToAccountAsync(targets, "acct", h.Folder("inbox"), "Inbox", Ct);
        Assert.False(same.IsOk);
        Assert.Contains("already in that account", same.Error!.Message, StringComparison.Ordinal);
        Assert.Empty(h.Transfer.Copies);

        h.Transfer.Failure = "It no longer exists.";
        var failed = await h.Actions.CopyToAccountAsync(targets, "b", "p", "Projects", Ct);
        Assert.False(failed.IsOk);
        Assert.True(h.Toasts.LastShown!.IsError);
        Assert.Contains("It no longer exists.", h.Toasts.LastShown.Message, StringComparison.Ordinal);
    }

    private sealed class PickFolders(CopyToAccountAnswer? answer) : IDialogService
    {
        public CopyToAccountDialogRequest? Asked { get; private set; }

        public bool IsOpen => false;

        public Task<TResult?> ShowAsync<TResult>(DialogRequest<TResult> request, CancellationToken ct)
        {
            if (request is CopyToAccountDialogRequest copy)
            {
                Asked = copy;
            }

            return Task.FromResult((TResult?)(object?)answer);
        }
    }
}
