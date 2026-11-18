// Mirrors: spec/menus.md MENU-THREAD, MENU-ADDR, MENU-BODY; Bulwark components/email/conversation-message-menu.tsx, address-context-menu.tsx,
// reader-selection-menu.tsx; Rust reader.rs menu :986 and ctxmenu.rs request :150 (the "body" and "addr" kinds). Owner menus.

using Barbican.Presentation.Commands;
using static Barbican.Presentation.Menus.MenuItems;

namespace Barbican.Presentation.Menus;

/// <summary>The menus of the reader area: a message inside a thread, an address, the body.</summary>
public static class MessageMenus
{
    /// <summary>MENU-THREAD: a message inside an open thread.</summary>
    public static IReadOnlyList<MenuItemSpec> Thread(MenuContext c, IMenuDataSource d)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(d);
        if (c.Targets.Count == 0)
        {
            return [];
        }

        var first = c.Targets[0];
        var rights = d.Folder(first.AccountKey, c.Mailbox?.MailboxId)?.Rights ?? MailboxRights.Full;
        return
        [
            Item(MenuGlyphs.Open, "Open", CommandIds.OpenMessage),
            Item(MenuGlyphs.Reply, "Reply", CommandIds.Reply, "R"),
            Item(MenuGlyphs.ReplyAll, "Reply all", CommandIds.ReplyAll, "Shift+R"),
            Item(MenuGlyphs.Forward, "Forward", CommandIds.Forward, "F"),
            Sep(),
            first.Keywords.IsSet("$seen")
                ? Item(MenuGlyphs.Unread, "Mark as unread", CommandIds.MarkUnread, "U", enabled: rights.MaySetSeen)
                : Item(MenuGlyphs.Read, "Mark as read", CommandIds.MarkRead, "U", enabled: rights.MaySetSeen),
            first.Keywords.IsSet("$flagged")
                ? Item(MenuGlyphs.Starred, "Unstar", CommandIds.Unstar, "S", enabled: rights.MaySetKeywords)
                : Item(MenuGlyphs.Star, "Star", CommandIds.Star, "S", enabled: rights.MaySetKeywords),
            Sep(),
            Item(MenuGlyphs.Print, "Print", CommandIds.Print),
            Danger(MenuGlyphs.Delete, "Delete", CommandIds.Delete, "Del", enabled: rights.MayRemoveItems),
        ];
    }

    /// <summary>MENU-ADDR: an address in the reader header or a chip; no menu when no address can be parsed.</summary>
    public static IReadOnlyList<MenuItemSpec> Address(MenuContext c)
    {
        ArgumentNullException.ThrowIfNull(c);
        var address = c.Address?.Trim();
        if (string.IsNullOrEmpty(address) || !address.Contains('@', StringComparison.Ordinal))
        {
            return [];
        }

        return
        [
            Header(address),
            Item(MenuGlyphs.Copy, "Copy email address", CommandIds.CopyAddress, argument: address),
            Item(MenuGlyphs.Reply, "New message", CommandIds.NewMessageTo, argument: address),
            Item(MenuGlyphs.Contact, "View contact", CommandIds.ViewContact, argument: address),
            Item(MenuGlyphs.Contact, "Add to contacts", CommandIds.AddToContacts, argument: address),
            Sep(),
            Item(MenuGlyphs.Search, "Find emails from this sender", CommandIds.FindFromSender, argument: address),
        ];
    }

    /// <summary>MENU-BODY: Copy and Select all in the plain-text view, then Copy message text, Reply and Print.</summary>
    public static IReadOnlyList<MenuItemSpec> Body(IMenuDataSource d)
    {
        ArgumentNullException.ThrowIfNull(d);
        var items = new List<MenuItemSpec>();
        if (d.BodyIsPlainText)
        {
            items.Add(Item(MenuGlyphs.Copy, "Copy", CommandIds.BodyCopy, "Ctrl+C"));
            items.Add(Item(MenuGlyphs.Select, "Select all", CommandIds.BodySelectAll, "Ctrl+A"));
            items.Add(Sep());
        }

        items.Add(Item(MenuGlyphs.Copy, "Copy message text", CommandIds.CopyMessageText));
        items.Add(Item(MenuGlyphs.Reply, "Reply", CommandIds.Reply, "R"));
        items.Add(Item(MenuGlyphs.Print, "Print", CommandIds.Print));
        return items;
    }
}
