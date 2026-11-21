// Test double for IMenuDataSource: fixed folders (with rights) and tags. Owner menus.

using Barbican.Core.Mail.Model;
using Barbican.Presentation.Menus;

namespace Barbican.Presentation.Tests.Menus;

internal sealed class FakeMenuData : IMenuDataSource
{
    public List<MenuFolder> FolderList { get; } =
    [
        new("inbox", "Inbox", MailboxRole.Inbox, 0, MailboxRights.Full),
        new("drafts", "Drafts", MailboxRole.Drafts, 0, MailboxRights.Full),
        new("sent", "Sent", MailboxRole.Sent, 0, MailboxRights.Full),
        new("archive", "Archive", MailboxRole.Archive, 0, MailboxRights.Full),
        new("junk", "Spam", MailboxRole.Junk, 0, MailboxRights.Full),
        new("trash", "Trash", MailboxRole.Trash, 0, MailboxRights.Full),
        new("work", "Work", MailboxRole.None, 0, MailboxRights.Full),
        new("q4", "Q4", MailboxRole.None, 1, MailboxRights.Full),
        new("__scheduled", "Scheduled", MailboxRole.None, 0, MailboxRights.Full),
    ];

    public List<MenuTag> TagList { get; } = [new("work", "Work", "blue", 0x3B82F6), new("home", "Home", "green", 0x22C55E)];

    public bool NestedTags { get; set; }

    public bool ConfirmBulkDelete { get; set; } = true;

    public bool BodyIsPlainText { get; set; }

    public bool GroupListByDate { get; set; }

    public string? SortKey { get; set; } = ListSortChoices.NewestKey;

    public string? ListSortKey(MailboxRole role) => SortKey;

    public IReadOnlyList<MenuFolder> Folders(string? accountKey) => FolderList;

    public MenuFolder? Folder(string? accountKey, string? mailboxId) => FolderList.Find(f => string.Equals(f.Id, mailboxId, StringComparison.Ordinal));

    public IReadOnlyList<MenuTag> Tags() => TagList;
}
