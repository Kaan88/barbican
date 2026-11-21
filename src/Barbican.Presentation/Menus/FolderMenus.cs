// Mirrors: spec/menus.md MENU-FOLDER, MENU-LIST, FOLDERS-HEADER (I-049); Rust ctxmenu.rs folder_items :551 and confirmation labels :833; Bulwark
// components/layout/mailbox-context-menu.tsx:251-466, sidebar.tsx:2074 and :2455 (the folders-section menu), components/email/mail-list-menus.tsx:84-254. Owner menus.

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

        var all = d.Folders(c.Mailbox!.AccountKey);
        var folder = all.FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.Ordinal));
        var role = folder?.Role ?? RoleOf(c.Mailbox.Role);
        var rights = folder?.Rights ?? MailboxRights.Full;
        // Bulwark mailbox-context-menu.tsx:309-466. Trash and Junk hold mail on its way out: no subfolders, no imports, no rules (:313-316, :376, :401). The server's own folders cannot be renamed
        // or deleted: Rename is not shown for them (:380). A row whose data says there is nothing to do is off (:326-334, :433, :446).
        var emptyable = role is MailboxRole.Trash or MailboxRole.Junk;
        var items = new List<MenuItemSpec>
        {
            Header(folder is null ? string.Empty : PathOf(all, folder)),
            Item(MenuGlyphs.Read, "Mark folder as read", CommandIds.MarkFolderRead, enabled: rights.MaySetSeen && (folder?.Unread ?? -1) != 0),
        };
        if (folder is not null && HasChildren(all, folder))
        {
            items.Add(Item(MenuGlyphs.Read, "Mark folder & subfolders as read", CommandIds.MarkFolderRead, argument: "children", enabled: rights.MaySetSeen && UnreadInTree(all, folder) != 0));
        }

        items.Add(Sep());
        var manage = new List<MenuItemSpec>();
        if (!emptyable)
        {
            manage.Add(Item(MenuGlyphs.NewFolder, "New subfolder", CommandIds.NewSubfolder, enabled: rights.MayCreateChild));
        }

        if (role == MailboxRole.None)
        {
            manage.Add(Item(MenuGlyphs.Rename, "Rename", CommandIds.RenameFolder, enabled: rights.MayRename));
        }

        if (manage.Count > 0)
        {
            items.AddRange(manage);
            items.Add(Sep());
        }

        items.AddRange(TransferRows(folder, rights, emptyable));
        items.AddRange(DangerRows(folder, role, rights));
        return items;
    }

    // Import (not in Trash or Junk), Export (off for an empty folder) and Refresh: mailbox-context-menu.tsx:401-431
    private static List<MenuItemSpec> TransferRows(MenuFolder? folder, MailboxRights rights, bool emptyable)
    {
        var rows = new List<MenuItemSpec>();
        if (!emptyable)
        {
            rows.Add(Item(MenuGlyphs.Upload, "Import messages (.eml, .mbox)…", CommandIds.ImportMessages, enabled: rights.MayAddItems));
        }

        var empty = folder?.Total == 0;
        rows.Add(Item(MenuGlyphs.Download, "Export as mbox…", CommandIds.ExportMbox, enabled: !empty));
        rows.Add(Item(MenuGlyphs.Download, "Export as .eml files…", CommandIds.ExportEml, enabled: !empty));
        rows.Add(Item(MenuGlyphs.Refresh, "Refresh", CommandIds.RefreshFolder));
        return rows;
    }

    // the destructive rows come last, after a rule (R6, :433-466)
    private static List<MenuItemSpec> DangerRows(MenuFolder? folder, MailboxRole role, MailboxRights rights)
    {
        var rows = new List<MenuItemSpec>();
        if (role is MailboxRole.Trash or MailboxRole.Junk)
        {
            rows.Add(Sep());
            rows.Add(Danger(MenuGlyphs.Delete, role == MailboxRole.Trash ? "Empty trash" : "Empty spam", role == MailboxRole.Trash ? CommandIds.EmptyTrash : CommandIds.EmptySpam, confirm: EmptyConfirm, enabled: rights.MayRemoveItems && folder?.Total != 0));
        }

        if (role == MailboxRole.None)
        {
            rows.Add(Sep());
            rows.Add(Danger(MenuGlyphs.Delete, "Delete folder", CommandIds.DeleteFolder, confirm: DeleteConfirm, enabled: rights.MayDelete));
        }

        return rows;
    }

    /// <summary>FOLDERS-HEADER (sidebar.tsx:2455, mailbox-context-menu.tsx:251-282): mark all folders read, —, new folder, refresh (I-049).</summary>
    public static IReadOnlyList<MenuItemSpec> FoldersHeader() =>
    [
        Item(MenuGlyphs.Read, "Mark all folders as read", CommandIds.MarkAllFoldersRead),
        Sep(),
        Item(MenuGlyphs.NewFolder, "New folder", CommandIds.NewFolder),
        Item(MenuGlyphs.Refresh, "Refresh", CommandIds.RefreshFolder),
    ];

    /// <summary>
    /// MENU-LIST-ACTIONS, the list toolbar's folder-actions button: Refresh, Sort ›, Group by date, —, Mark folder as read, Mark all folders as read and, in Trash and Spam only, —, Empty trash / Empty spam
    /// (Bulwark components/email/mail-list-menus.tsx:141-254, mail-app.tsx:6672-6684: <c>folderIsEmptyable</c> is Spam and Trash).
    /// </summary>
    public static IReadOnlyList<MenuItemSpec> ListActions(MenuContext c, IMenuDataSource d)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(d);
        var folder = d.Folder(c.Mailbox?.AccountKey, c.Mailbox?.MailboxId);
        var role = folder?.Role ?? RoleOf(c.Mailbox?.Role);
        var rights = folder?.Rights ?? MailboxRights.Full;
        var items = new List<MenuItemSpec>
        {
            Item(MenuGlyphs.Refresh, "Refresh", CommandIds.RefreshList),
            Sub("Sort", "Sort", MenuIds.ListSort),
            Item(string.Empty, "Group by date", CommandIds.ToggleGroupByDate, check: d.GroupListByDate),
            Sep(),
            Item(MenuGlyphs.Read, "Mark folder as read", CommandIds.MarkFolderRead, enabled: folder is not null && rights.MaySetSeen && folder.Unread != 0),
            Item(MenuGlyphs.Read, "Mark all folders as read", CommandIds.MarkAllFoldersRead),
        };
        if (folder is not null && role is MailboxRole.Trash or MailboxRole.Junk)
        {
            items.Add(Sep());
            items.Add(Danger(MenuGlyphs.Delete, role == MailboxRole.Trash ? "Empty trash" : "Empty spam", role == MailboxRole.Trash ? CommandIds.EmptyTrash : CommandIds.EmptySpam, confirm: EmptyConfirm, enabled: rights.MayRemoveItems && folder.Total != 0));
        }

        return items;
    }

    /// <summary>The Sort flyout (mail-list-menus.tsx:84-127): the orders as radio rows with the current one checked, and the custom order when Settings made one.</summary>
    public static IReadOnlyList<MenuItemSpec> ListSort(MenuContext c, IMenuDataSource d)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(d);
        var role = d.Folder(c.Mailbox?.AccountKey, c.Mailbox?.MailboxId)?.Role ?? RoleOf(c.Mailbox?.Role);
        var current = d.ListSortKey(role);
        var items = ListSortChoices.All.Select(choice => Item(string.Empty, choice.Label, CommandIds.SortList, argument: choice.Key, check: string.Equals(choice.Key, current, StringComparison.Ordinal))).ToList();
        if (current is null)
        {
            items.Add(Sep());
            items.Add(new MenuItemSpec(MenuItemKind.Item, ListSortChoices.CustomLabel, Checked: true));
        }

        return items;
    }

    private static bool HasChildren(IReadOnlyList<MenuFolder> all, MenuFolder folder)
    {
        var at = IndexOf(all, folder);
        return at >= 0 && at + 1 < all.Count && all[at + 1].Depth > folder.Depth;
    }

    /// <summary>The unread count of a folder and everything below it (-1 when a count is unknown), unreadInTree of mailbox-context-menu.tsx:325.</summary>
    private static int UnreadInTree(IReadOnlyList<MenuFolder> all, MenuFolder folder)
    {
        var at = IndexOf(all, folder);
        if (at < 0 || folder.Unread < 0)
        {
            return -1;
        }

        var sum = folder.Unread;
        for (var i = at + 1; i < all.Count && all[i].Depth > folder.Depth; i++)
        {
            if (all[i].Unread < 0)
            {
                return -1;
            }

            sum += all[i].Unread;
        }

        return sum;
    }

    /// <summary>The folder's path, "Work / Q4" (the header shows it, getMailboxPath).</summary>
    private static string PathOf(IReadOnlyList<MenuFolder> all, MenuFolder folder)
    {
        var at = IndexOf(all, folder);
        if (at < 0)
        {
            return folder.Name;
        }

        var parts = new List<string> { folder.Name };
        var depth = folder.Depth;
        for (var i = at - 1; i >= 0 && depth > 0; i--)
        {
            if (all[i].Depth < depth)
            {
                parts.Insert(0, all[i].Name);
                depth = all[i].Depth;
            }
        }

        return string.Join(" / ", parts);
    }

    private static int IndexOf(IReadOnlyList<MenuFolder> all, MenuFolder folder)
    {
        for (var i = 0; i < all.Count; i++)
        {
            if (string.Equals(all[i].Id, folder.Id, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

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
