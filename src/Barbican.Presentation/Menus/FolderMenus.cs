// Mirrors: spec/menus.md MENU-FOLDER, MENU-LIST, FOLDERS-HEADER (I-049); Rust ctxmenu.rs folder_items :551 and confirmation labels :833; Bulwark
// components/layout/mailbox-context-menu.tsx, sidebar.tsx:2074 and :2455 (the folders-section menu), mail-list-menus.tsx. Owner menus.

using Barbican.Core.Mail.Model;
using Barbican.Presentation.Commands;
using static Barbican.Presentation.Menus.MenuItems;

namespace Barbican.Presentation.Menus;

/// <summary>The menus of the folders: one folder, the folders header, the list background.</summary>
public static class FolderMenus
{
    /// <summary>The confirmation's danger label for emptying Trash or Spam (ctxmenu.rs:834).</summary>
    public const string EmptyConfirm = "Delete all messages permanently";

    /// <summary>The confirmation's danger label for deleting a folder (ctxmenu.rs:835).</summary>
    public const string DeleteConfirm = "Delete this folder";

    /// <summary>MENU-FOLDER; virtual folders have no menu.</summary>
    public static IReadOnlyList<MenuItemSpec> Folder(MenuContext c, IMenuDataSource d)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(d);
        var id = c.Mailbox?.MailboxId;
        if (id is null || id.StartsWith("__", StringComparison.Ordinal))
        {
            return [];
        }

        var folder = d.Folder(c.Mailbox!.AccountKey, id);
        var role = folder?.Role ?? RoleOf(c.Mailbox.Role);
        var rights = folder?.Rights ?? MailboxRights.Full;
        var items = new List<MenuItemSpec>
        {
            Header(folder?.Name ?? string.Empty),
            Item(MenuGlyphs.Read, "Mark folder as read", CommandIds.MarkFolderRead, enabled: rights.MaySetSeen),
            Item(MenuGlyphs.Refresh, "Refresh", CommandIds.RefreshFolder),
            Sep(),
            Item(MenuGlyphs.NewFolder, "New subfolder", CommandIds.NewSubfolder, enabled: rights.MayCreateChild),
            Item(MenuGlyphs.Rename, "Rename", CommandIds.RenameFolder, enabled: role == MailboxRole.None && rights.MayRename),
            Item(MenuGlyphs.Upload, "Import messages (.eml, .mbox)…", CommandIds.ImportMessages, enabled: rights.MayAddItems),
        };
        if (role is MailboxRole.Trash or MailboxRole.Junk)
        {
            items.Add(Sep());
            items.Add(Danger(MenuGlyphs.Delete, role == MailboxRole.Trash ? "Empty trash" : "Empty spam", role == MailboxRole.Trash ? CommandIds.EmptyTrash : CommandIds.EmptySpam, confirm: EmptyConfirm, enabled: rights.MayRemoveItems));
        }

        if (role == MailboxRole.None)
        {
            items.Add(Sep());
            items.Add(Danger(MenuGlyphs.Delete, "Delete folder", CommandIds.DeleteFolder, confirm: DeleteConfirm, enabled: rights.MayDelete));
        }

        return items;
    }

    /// <summary>FOLDERS-HEADER: mark all read, new folder, refresh (I-049).</summary>
    public static IReadOnlyList<MenuItemSpec> FoldersHeader() =>
    [
        Item(MenuGlyphs.Read, "Mark all as read", CommandIds.MarkFolderRead, argument: "all"),
        Item(MenuGlyphs.NewFolder, "New folder", CommandIds.NewFolder),
        Item(MenuGlyphs.Refresh, "Refresh", CommandIds.RefreshFolder),
    ];

    /// <summary>MENU-LIST: Refresh, Mark folder as read, Select all.</summary>
    public static IReadOnlyList<MenuItemSpec> List(MenuContext c, IMenuDataSource d)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(d);
        var rights = d.Folder(c.Mailbox?.AccountKey, c.Mailbox?.MailboxId)?.Rights ?? MailboxRights.Full;
        return
        [
            Item(MenuGlyphs.Refresh, "Refresh", CommandIds.RefreshList),
            Item(MenuGlyphs.Read, "Mark folder as read", CommandIds.MarkFolderRead, enabled: rights.MaySetSeen),
            Item(MenuGlyphs.Select, "Select all", CommandIds.SelectAll, "Ctrl+A"),
        ];
    }

    private static MailboxRole RoleOf(string? role) => role?.ToLowerInvariant() switch
    {
        "inbox" => MailboxRole.Inbox,
        "drafts" => MailboxRole.Drafts,
        "sent" => MailboxRole.Sent,
        "trash" => MailboxRole.Trash,
        "junk" => MailboxRole.Junk,
        "archive" => MailboxRole.Archive,
        null or "" => MailboxRole.None,
        _ => MailboxRole.Important,
    };
}
