// New tests: every menu of the menus stream against spec/menus.md item by item (MENU-MAIL, -THREAD, -ADDR, -BODY, -FOLDER, -TAG, -LIST, FOLDERS-HEADER, ACCOUNT), for one and
// several messages, the Trash, Junk and Archive folders, role and custom folders, with and without tags, and read-only rights (J-048). Owner menus.

#pragma warning disable MA0002, MA0006, VSTHRD003, xUnit1051 // asserts compare labels ordinally by nature; fakes hand back TaskCompletionSource tasks; the delays are one-tick yields

using Barbican.Core.Mail.Model;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Menus;
using static Barbican.Presentation.Tests.Menus.MenuKit;

namespace Barbican.Presentation.Tests.Menus;

public sealed class MenuItemListTests
{
    private static readonly string[] MailOne =
    [
        "Reply", "Reply all", "Forward", "—", "Archive", "Delete", "Move to ›", "—", "Mark as read", "Star", "Tag ›", "—", "Report spam", "—", "More actions ›",
    ];

    [Fact]
    public void Mail_menu_for_one_message_lists_the_spec_rows_with_reply_forward_enabled()
    {
        var data = new FakeMenuData();
        var rows = MailMenus.Main(Context(MenuIds.Mail, targets: Target("m1")), data);
        Assert.Equal(MailOne, Labels(rows));
        Assert.All(rows.Where(r => r.Label is "Reply" or "Reply all" or "Forward"), r => Assert.True(r.Enabled));
        var hints = rows.Where(r => r.Hint is not null).ToDictionary(r => r.Label, r => r.Hint, StringComparer.Ordinal);
        Assert.Equal("R", hints["Reply"]);
        Assert.Equal("Shift+R", hints["Reply all"]);
        Assert.Equal("F", hints["Forward"]);
        Assert.Equal("E", hints["Archive"]);
        Assert.Equal("Del", hints["Delete"]);
        Assert.Equal("U", hints["Mark as read"]);
        Assert.Equal("S", hints["Star"]);
        Assert.Equal("!", hints["Report spam"]);
    }

    [Fact]
    public void Mail_menu_for_several_messages_adds_the_header_and_disables_single_message_rows()
    {
        var data = new FakeMenuData();
        var rows = MailMenus.Main(Context(MenuIds.Mail, targets: [Target("m1"), Target("m2"), Target("m3")]), data);
        Assert.Equal("# 3 selected", Labels(rows)[0]);
        Assert.All(rows.Where(r => r.Label is "Reply" or "Reply all" or "Forward"), r => Assert.False(r.Enabled));
        Assert.True(rows.Single(r => r.Label == "Archive").Enabled);
        // deleting several asks first (confirmBulkDelete is on by default); one message does not
        Assert.Equal("Delete selected messages", rows.Single(r => r.Label == "Delete").ConfirmLabel);
        Assert.Null(MailMenus.Main(Context(MenuIds.Mail, targets: Target("m1")), data).Single(r => r.Label == "Delete").ConfirmLabel);
        data.ConfirmBulkDelete = false;
        Assert.Null(MailMenus.Main(Context(MenuIds.Mail, targets: [Target("m1"), Target("m2")]), data).Single(r => r.Label == "Delete").ConfirmLabel);
    }

    [Fact]
    public void Read_and_star_rows_follow_the_first_message()
    {
        var data = new FakeMenuData();
        var rows = MailMenus.Main(Context(MenuIds.Mail, targets: Target("m1", "$seen", "$flagged")), data);
        Assert.Contains("Mark as unread", Labels(rows));
        Assert.Contains("Unstar", Labels(rows));
        Assert.Equal(CommandIds.MarkUnread, rows.Single(r => r.Label == "Mark as unread").CommandId);
        Assert.Equal(CommandIds.Unstar, rows.Single(r => r.Label == "Unstar").CommandId);
    }

    [Fact]
    public void In_the_junk_folder_the_spam_row_says_not_spam()
    {
        var rows = MailMenus.Main(Context(MenuIds.Mail, role: "junk", mailboxId: "junk", targets: Target("m1")), new FakeMenuData());
        Assert.Contains("Not spam", Labels(rows));
        Assert.DoesNotContain("Report spam", Labels(rows));
        Assert.Equal(CommandIds.NotSpam, rows.Single(r => r.Label == "Not spam").CommandId);
    }

    [Fact]
    public void In_Trash_a_Restore_row_comes_first_then_a_separator_before_Archive()
    {
        // Bulwark message-actions.ts:271-274
        var rows = MailMenus.Main(Context(MenuIds.Mail, role: "trash", mailboxId: "trash", targets: Target("m1")), new FakeMenuData());
        Assert.Equal(["Reply", "Reply all", "Forward", "—", "Restore", "—", "Archive", "Delete", "Move to ›", "—", "Mark as read", "Star", "Tag ›", "—", "Report spam", "—", "More actions ›"], Labels(rows));
        Assert.Equal(CommandIds.Restore, rows.Single(r => r.Label == "Restore").CommandId);
    }

    [Theory]
    [InlineData("archive", "archive")]
    [InlineData(null, "q-in-archive")]
    public void In_Archive_or_below_it_the_Archive_row_says_Move_to_Inbox(string? role, string id)
    {
        // Bulwark message-actions.ts:275-277, lib/archive-inbox.ts:10-28
        var data = new FakeMenuData();
        data.FolderList.Insert(data.FolderList.FindIndex(f => f.Id == "archive") + 1, new MenuFolder("q-in-archive", "Old", MailboxRole.None, 1, MailboxRights.Full));
        var rows = MailMenus.Main(Context(MenuIds.Mail, role: role, mailboxId: id, targets: Target("m1")), data);
        Assert.DoesNotContain("Archive", Labels(rows));
        Assert.Equal(CommandIds.MoveToInbox, rows.Single(r => r.Label == "Move to Inbox").CommandId);
    }

    [Fact]
    public void Outside_Trash_and_Archive_there_is_no_Restore_or_Move_to_Inbox()
    {
        var rows = MailMenus.Main(Context(MenuIds.Mail, role: "none", mailboxId: "work", targets: Target("m1")), new FakeMenuData());
        Assert.Equal(MailOne, Labels(rows));
    }

    [Fact]
    public void Move_to_lists_real_folders_indented_without_Drafts_Sent_the_current_folder_or_virtual_ones()
    {
        var rows = MailMenus.Move(Context(MenuIds.MailMove, targets: Target("m1")), new FakeMenuData());
        Assert.Equal(["Archive", "Spam", "Trash", "Work", "Q4"], rows.Select(r => r.Label).ToArray());
        Assert.Equal(1, rows.Single(r => r.Label == "Q4").Indent);
        Assert.All(rows, r => Assert.Equal(CommandIds.MoveTo, r.CommandId));
        Assert.Equal("q4", rows.Single(r => r.Label == "Q4").Argument);
    }

    [Fact]
    public void Move_to_leaves_out_folders_that_take_no_new_mail()
    {
        var data = new FakeMenuData();
        data.FolderList[data.FolderList.FindIndex(f => f.Id == "work")] = new MenuFolder("work", "Work", MailboxRole.None, 0, new MailboxRights(MayAddItems: false));
        Assert.DoesNotContain(MailMenus.Move(Context(MenuIds.MailMove, targets: Target("m1")), data), r => r.Label == "Work");
    }

    [Fact]
    public void Tag_flyout_checks_the_tags_of_the_first_message_and_says_when_there_are_none()
    {
        var data = new FakeMenuData();
        var rows = MailMenus.Tag(Context(MenuIds.MailTag, targets: Target("m1", "$label:work")), data);
        Assert.Equal(["Work", "Home"], rows.Select(r => r.Label).ToArray());
        Assert.True(rows[0].Checked);
        Assert.False(rows[1].Checked);
        Assert.Equal(CommandIds.Untag, rows[0].CommandId);
        Assert.Equal(CommandIds.Tag, rows[1].CommandId);
        Assert.Equal(MenuGlyphs.Dot(0x3B82F6), rows[0].Icon);
        data.TagList.Clear();
        var none = MailMenus.Tag(Context(MenuIds.MailTag, targets: Target("m1")), data);
        Assert.Equal("No tags", Assert.Single(none).Label);
        Assert.False(none[0].Enabled);
    }

    [Fact]
    public void More_actions_are_all_disabled_for_several_messages()
    {
        var one = MailMenus.More(Context(MenuIds.MailMore, targets: Target("m1")));
        Assert.Equal(["Open", "Print", "Download (.eml)", "View source", "Forward as attachment", "—", "Copy sender address", "Find emails from sender"], Labels(one));
        Assert.All(one.Where(r => r.Kind == MenuItemKind.Item), r => Assert.True(r.Enabled));
        var many = MailMenus.More(Context(MenuIds.MailMore, targets: [Target("m1"), Target("m2")]));
        Assert.All(many.Where(r => r.Kind == MenuItemKind.Item), r => Assert.False(r.Enabled));
    }

    [Fact]
    public void A_read_only_folder_disables_the_write_rows()
    {
        var data = new FakeMenuData();
        data.FolderList[0] = new MenuFolder("inbox", "Inbox", MailboxRole.Inbox, 0, new MailboxRights(MayRemoveItems: false, MaySetSeen: false, MaySetKeywords: false));
        var rows = MailMenus.Main(Context(MenuIds.Mail, targets: Target("m1")), data);
        foreach (var label in new[] { "Archive", "Delete", "Move to", "Mark as read", "Star", "Tag", "Report spam" })
        {
            Assert.False(rows.Single(r => r.Label == label).Enabled, label);
        }

        Assert.True(rows.Single(r => r.Label == "Reply").Enabled);
    }

    [Fact]
    public void Thread_menu_lists_the_spec_rows()
    {
        var rows = MessageMenus.Thread(Context(MenuIds.Thread, targets: Target("m1")), new FakeMenuData());
        Assert.Equal(["Open", "Reply", "Reply all", "Forward", "—", "Mark as read", "Star", "—", "Print", "Delete"], Labels(rows));
        Assert.True(rows[^1].Danger);
        var seen = MessageMenus.Thread(Context(MenuIds.Thread, targets: Target("m1", "$seen", "$flagged")), new FakeMenuData());
        Assert.Contains("Mark as unread", Labels(seen));
        Assert.Contains("Unstar", Labels(seen));
    }

    [Fact]
    public void Address_menu_lists_the_spec_rows_and_is_empty_without_an_address()
    {
        var rows = MessageMenus.Address(Context(MenuIds.Address, address: "ann@example.org"));
        Assert.Equal(["# ann@example.org", "Copy email address", "New message", "View contact", "Add to contacts", "—", "Find emails from this sender"], Labels(rows));
        Assert.All(rows.Where(r => r.Kind == MenuItemKind.Item), r => Assert.Equal("ann@example.org", r.Argument));
        Assert.Empty(MessageMenus.Address(Context(MenuIds.Address, address: "not an address")));
        Assert.Empty(MessageMenus.Address(Context(MenuIds.Address)));
    }

    [Fact]
    public void Body_menu_adds_copy_and_select_all_only_for_plain_text()
    {
        var data = new FakeMenuData();
        Assert.Equal(["Copy message text", "Reply", "Print"], Labels(MessageMenus.Body(data)));
        data.BodyIsPlainText = true;
        Assert.Equal(["Copy", "Select all", "—", "Copy message text", "Reply", "Print"], Labels(MessageMenus.Body(data)));
    }

    [Fact]
    public void Folder_menu_for_a_custom_folder_has_rename_and_a_confirmed_delete()
    {
        var rows = FolderMenus.Folder(Context(MenuIds.Folder, role: null, mailboxId: "work"), new FakeMenuData());
        Assert.Equal(["# Work", "Mark folder as read", "—", "New subfolder", "Rename", "—", "Import messages (.eml, .mbox)…", "Export as mbox…", "Export as .eml files…", "Refresh", "—", "Delete folder"], Labels(rows));
        Assert.True(rows.Single(r => r.Label == "Rename").Enabled);
        var delete = rows.Single(r => r.Label == "Delete folder");
        Assert.True(delete.Danger);
        Assert.Equal("Delete this folder", delete.ConfirmLabel);
    }

    [Fact]
    public void Folder_menu_for_a_role_folder_cannot_rename_and_trash_and_spam_empty_with_a_confirmation()
    {
        var data = new FakeMenuData();
        var inbox = FolderMenus.Folder(Context(MenuIds.Folder, mailboxId: "inbox"), data);
        Assert.False(inbox.Single(r => r.Label == "Rename").Enabled);
        Assert.DoesNotContain(inbox, r => r.Danger);

        var trash = FolderMenus.Folder(Context(MenuIds.Folder, role: "trash", mailboxId: "trash"), data);
        var empty = trash.Single(r => r.Danger);
        Assert.Equal("Empty trash", empty.Label);
        Assert.Equal("Delete all messages permanently", empty.ConfirmLabel);
        Assert.Equal("Empty spam", FolderMenus.Folder(Context(MenuIds.Folder, role: "junk", mailboxId: "junk"), data).Single(r => r.Danger).Label);
    }

    [Fact]
    public void Folder_menu_is_empty_for_virtual_folders_and_respects_rights()
    {
        var data = new FakeMenuData();
        Assert.Empty(FolderMenus.Folder(Context(MenuIds.Folder, role: null, mailboxId: "__scheduled"), data));
        data.FolderList[data.FolderList.FindIndex(f => f.Id == "work")] = new MenuFolder("work", "Work", MailboxRole.None, 0, new MailboxRights(MayDelete: false, MayCreateChild: false, MayRename: false));
        var rows = FolderMenus.Folder(Context(MenuIds.Folder, role: null, mailboxId: "work"), data);
        Assert.False(rows.Single(r => r.Label == "Delete folder").Enabled);
        Assert.False(rows.Single(r => r.Label == "New subfolder").Enabled);
        Assert.False(rows.Single(r => r.Label == "Rename").Enabled);
    }

    [Fact]
    public void Tag_menu_lists_the_spec_rows_and_offers_a_nested_tag_only_with_nested_tags()
    {
        var data = new FakeMenuData();
        var rows = TagMenus.Tag(Context(MenuIds.Tag, tag: "work"), data);
        Assert.Equal(["Show messages", "Mark all as read", "—", "New tag…", "Rename…", "Colour ›", "Delete tag", "—", "Manage tags…"], Labels(rows));
        Assert.Equal("Delete this tag (messages keep it)", rows.Single(r => r.Label == "Delete tag").ConfirmLabel);
        data.NestedTags = true;
        Assert.Contains("New tag inside…", Labels(TagMenus.Tag(Context(MenuIds.Tag, tag: "work"), data)));
        Assert.Empty(TagMenus.Tag(Context(MenuIds.Tag), data));
    }

    [Fact]
    public void Tag_colour_flyout_checks_the_current_colour()
    {
        var rows = TagMenus.Colour(Context(MenuIds.TagColour, tag: "work"), new FakeMenuData());
        Assert.Equal(Barbican.Core.Mail.Tags.TagRules.Palette.Count, rows.Count);
        Assert.Equal("Blue", rows.Single(r => r.Checked == true).Label);
        Assert.Equal("work|blue", rows.Single(r => r.Checked == true).Argument);
    }

    [Fact]
    public void List_and_folders_header_menus_list_the_spec_rows()
    {
        var data = new FakeMenuData();
        Assert.Equal(["Refresh", "Mark folder as read", "Select all"], Labels(FolderMenus.List(Context(MenuIds.List), data)));
        Assert.Equal(["Mark all as read", "New folder", "Refresh"], Labels(FolderMenus.FoldersHeader()));
    }

    [Fact]
    public void Account_menu_asks_before_signing_out_and_shows_manage_accounts_disabled()
    {
        var rows = AccountMenus.Account(Context(MenuIds.Account, address: "acct"));
        Assert.Equal(["Set as default", "—", "Sign out", "Sign out of all accounts", "—", "Manage accounts"], Labels(rows));
        Assert.Equal("Sign out of this account", rows.Single(r => r.Label == "Sign out").ConfirmLabel);
        Assert.Equal("Sign out of every account", rows.Single(r => r.Label == "Sign out of all accounts").ConfirmLabel);
        Assert.False(rows[^1].Enabled);
        Assert.Empty(AccountMenus.Account(Context(MenuIds.Account)));
    }

    [Fact]
    public void Every_command_a_menu_row_names_is_a_command_id()
    {
        var known = AllCommands().Keys.ToHashSet(StringComparer.Ordinal);
        var data = new FakeMenuData();
        var all = new List<MenuItemSpec>();
        var t = Target("m1");
        all.AddRange(MailMenus.Main(Context(MenuIds.Mail, targets: t), data));
        all.AddRange(MailMenus.Move(Context(MenuIds.MailMove, targets: t), data));
        all.AddRange(MailMenus.Tag(Context(MenuIds.MailTag, targets: t), data));
        all.AddRange(MailMenus.More(Context(MenuIds.MailMore, targets: t)));
        all.AddRange(MessageMenus.Thread(Context(MenuIds.Thread, targets: t), data));
        all.AddRange(MessageMenus.Address(Context(MenuIds.Address, address: "a@b.example")));
        all.AddRange(MessageMenus.Body(data));
        all.AddRange(FolderMenus.Folder(Context(MenuIds.Folder, role: null, mailboxId: "work"), data));
        all.AddRange(FolderMenus.FoldersHeader());
        all.AddRange(FolderMenus.List(Context(MenuIds.List), data));
        all.AddRange(TagMenus.Tag(Context(MenuIds.Tag, tag: "work"), data));
        all.AddRange(AccountMenus.Account(Context(MenuIds.Account, address: "acct")));
        Assert.All(all.Where(i => i.CommandId is { Length: > 0 }), i => Assert.Contains(i.CommandId!, known));
    }
}
