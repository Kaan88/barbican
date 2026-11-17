// Mirrors: spec/menus.md MENU-MAIL; Rust ctxmenu.rs mail_items :516, submenu "move"/"tag"/"more" :575, confirmation labels :833-:852; Bulwark
// components/email/email-context-menu.tsx, mail-list-menus.tsx, message-actions.ts. myRights gate the write rows (J-048, RFC 8621 section 2.4). Owner menus.

using Barbican.Core.Mail.Model;
using Barbican.Presentation.Commands;
using static Barbican.Presentation.Menus.MenuItems;

namespace Barbican.Presentation.Menus;

/// <summary>MENU-MAIL (a message row) and its three flyouts, as pure functions over the context and the data source.</summary>
public static class MailMenus
{
    /// <summary>The confirmation's danger label for deleting several messages (ctxmenu.rs:850).</summary>
    public const string BulkDeleteConfirm = "Delete selected messages";

    /// <summary>The main menu.</summary>
    public static IReadOnlyList<MenuItemSpec> Main(MenuContext c, IMenuDataSource d)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(d);
        if (c.Targets.Count == 0)
        {
            return [];
        }

        var n = c.Targets.Count;
        var first = c.Targets[0];
        var one = n == 1;
        var rights = Rights(c, d);
        var junk = string.Equals(c.Mailbox?.Role, "junk", StringComparison.Ordinal);
        var seen = first.Keywords.IsSet("$seen");
        var flagged = first.Keywords.IsSet("$flagged");
        var items = new List<MenuItemSpec>();
        if (!one)
        {
            items.Add(Header($"{n} selected"));
        }

        items.Add(Item(MenuGlyphs.Reply, "Reply", CommandIds.Reply, "R", enabled: one));
        items.Add(Item(MenuGlyphs.ReplyAll, "Reply all", CommandIds.ReplyAll, "Shift+R", enabled: one));
        items.Add(Item(MenuGlyphs.Forward, "Forward", CommandIds.Forward, "F", enabled: one));
        items.Add(Sep());
        if (string.Equals(c.Mailbox?.Role, "trash", StringComparison.Ordinal))
        {
            // Bulwark message-actions.ts:271-274: in Trash a "Restore" row, then a separator, before Archive.
            items.Add(Item(MenuGlyphs.Restore, "Restore", CommandIds.Restore, enabled: rights.MayRemoveItems));
            items.Add(Sep());
        }

        // Bulwark message-actions.ts:275-277, lib/archive-inbox.ts:10-28: inside Archive (or below it) the row reads "Move to Inbox".
        items.Add(InArchive(c, d)
            ? Item(MenuGlyphs.Inbox, "Move to Inbox", CommandIds.MoveToInbox, enabled: rights.MayRemoveItems)
            : Item(MenuGlyphs.Archive, "Archive", CommandIds.Archive, "E", enabled: rights.MayRemoveItems));
        items.Add(Danger(MenuGlyphs.Delete, "Delete", CommandIds.Delete, "Del", n > 1 && d.ConfirmBulkDelete ? BulkDeleteConfirm : null, enabled: rights.MayRemoveItems));
        items.Add(Sub(MenuGlyphs.Move, "Move to", MenuIds.MailMove, rights.MayRemoveItems));
        items.Add(Sep());
        items.Add(seen
            ? Item(MenuGlyphs.Unread, "Mark as unread", CommandIds.MarkUnread, "U", enabled: rights.MaySetSeen)
            : Item(MenuGlyphs.Read, "Mark as read", CommandIds.MarkRead, "U", enabled: rights.MaySetSeen));
        items.Add(flagged
            ? Item(MenuGlyphs.Starred, "Unstar", CommandIds.Unstar, "S", enabled: rights.MaySetKeywords)
            : Item(MenuGlyphs.Star, "Star", CommandIds.Star, "S", enabled: rights.MaySetKeywords));
        items.Add(Sub(MenuGlyphs.Tag, "Tag", MenuIds.MailTag, rights.MaySetKeywords));
        items.Add(Sep());
        items.Add(junk
            ? Item(MenuGlyphs.Spam, "Not spam", CommandIds.NotSpam, enabled: rights.MayRemoveItems)
            : Item(MenuGlyphs.Spam, "Report spam", CommandIds.ReportSpam, "!", enabled: rights.MayRemoveItems));
        items.Add(Sep());
        items.Add(Sub(MenuGlyphs.More, "More actions", MenuIds.MailMore));
        return items;
    }

    /// <summary>Move to: every folder except virtual ones, the current one and those that take no new mail, indented by depth.</summary>
    public static IReadOnlyList<MenuItemSpec> Move(MenuContext c, IMenuDataSource d)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(d);
        var account = c.Targets.Count > 0 ? c.Targets[0].AccountKey : c.Mailbox?.AccountKey;
        var items = new List<MenuItemSpec>();
        foreach (var f in d.Folders(account))
        {
            // Bulwark lib/move-menu.ts:117: no Drafts and no Sent either.
            if (f.IsVirtual || f.Role is MailboxRole.Drafts or MailboxRole.Sent || string.Equals(f.Id, c.Mailbox?.MailboxId, StringComparison.Ordinal) || !f.Rights.MayAddItems)
            {
                continue;
            }

            items.Add(Item(MenuGlyphs.Folder, f.Name, CommandIds.MoveTo, argument: f.Id, indent: f.Depth));
        }

        if (items.Count == 0)
        {
            items.Add(Note("No folders"));
        }

        return items;
    }

    /// <summary>Tag: every tag with its colour, checked when the first message carries <c>$label:id</c>; "No tags" when there are none.</summary>
    public static IReadOnlyList<MenuItemSpec> Tag(MenuContext c, IMenuDataSource d)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(d);
        var first = c.Targets.Count > 0 ? c.Targets[0] : null;
        var items = new List<MenuItemSpec>();
        foreach (var t in d.Tags())
        {
            var on = first?.Keywords.IsSet("$label:" + t.Id) ?? false;
            items.Add(Item(MenuGlyphs.Dot(t.ColorRgb), t.Label, on ? CommandIds.Untag : CommandIds.Tag, argument: t.Id, check: on));
        }

        if (items.Count == 0)
        {
            items.Add(Note("No tags"));
        }

        return items;
    }

    /// <summary>More actions: all disabled unless exactly one message is chosen.</summary>
    public static IReadOnlyList<MenuItemSpec> More(MenuContext c)
    {
        ArgumentNullException.ThrowIfNull(c);
        var one = c.Targets.Count == 1;
        return
        [
            Item(MenuGlyphs.Open, "Open", CommandIds.OpenSelected, "Enter", enabled: one),
            Item(MenuGlyphs.Print, "Print", CommandIds.Print, enabled: one),
            Item(MenuGlyphs.Download, "Download (.eml)", CommandIds.DownloadEml, enabled: one),
            Item("Tool", "View source", CommandIds.ViewSource, enabled: one),
            Item(MenuGlyphs.Forward, "Forward as attachment", CommandIds.ForwardAsAttachment, enabled: one),
            Sep(),
            Item(MenuGlyphs.Copy, "Copy sender address", CommandIds.CopySenderAddress, enabled: one),
            Item(MenuGlyphs.Search, "Find emails from sender", CommandIds.FindFromSender, enabled: one),
        ];
    }

    /// <summary>True when the browsed folder is the Archive or below it, and an Inbox exists to move to (Bulwark lib/archive-inbox.ts:10-28).</summary>
    private static bool InArchive(MenuContext c, IMenuDataSource d)
    {
        var account = c.Targets.Count > 0 ? c.Targets[0].AccountKey : c.Mailbox?.AccountKey;
        var id = c.Mailbox?.MailboxId;
        if (id is null)
        {
            return false;
        }

        var folders = d.Folders(account);
        var at = -1;
        for (var i = 0; i < folders.Count; i++)
        {
            if (string.Equals(folders[i].Id, id, StringComparison.Ordinal))
            {
                at = i;
                break;
            }
        }

        if (at < 0 || !folders.Any(f => f.Role == MailboxRole.Inbox))
        {
            return false;
        }

        // The folder list is in tree order: the ancestors are the nearest earlier rows with a smaller depth.
        var depth = folders[at].Depth;
        if (folders[at].Role == MailboxRole.Archive)
        {
            return true;
        }

        for (var i = at - 1; i >= 0 && depth > 0; i--)
        {
            if (folders[i].Depth < depth)
            {
                depth = folders[i].Depth;
                if (folders[i].Role == MailboxRole.Archive)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static MailboxRights Rights(MenuContext c, IMenuDataSource d)
    {
        var account = c.Targets.Count > 0 ? c.Targets[0].AccountKey : c.Mailbox?.AccountKey;
        return d.Folder(account, c.Mailbox?.MailboxId)?.Rights ?? MailboxRights.Full;
    }
}
