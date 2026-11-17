// Mirrors: spec/menus.md MENU-MAIL; Rust ctxmenu.rs mail_items :516, submenu "move"/"tag"/"more" :575, confirmation labels :833-:852; Bulwark
// components/email/email-context-menu.tsx, mail-list-menus.tsx, message-actions.ts. myRights gate the write rows (J-048, RFC 8621 section 2.4). Owner menus.

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
        items.Add(Item(MenuGlyphs.Archive, "Archive", CommandIds.Archive, "E", enabled: rights.MayRemoveItems));
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
            if (f.IsVirtual || string.Equals(f.Id, c.Mailbox?.MailboxId, StringComparison.Ordinal) || !f.Rights.MayAddItems)
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

    private static MailboxRights Rights(MenuContext c, IMenuDataSource d)
    {
        var account = c.Targets.Count > 0 ? c.Targets[0].AccountKey : c.Mailbox?.AccountKey;
        return d.Folder(account, c.Mailbox?.MailboxId)?.Rights ?? MailboxRights.Full;
    }
}
