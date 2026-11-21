// Mirrors: spec/menus.md MENU-CONTACT, MENU-GROUP, MENU-CATEGORY, MENU-BOOK item by item; Rust ctxmenu.rs:314-330, :454; Bulwark components/contacts/contact-context-menu.tsx, contacts-sidebar.tsx. I-355, I-356, I-359.
// The providers read what the menu was opened on from MenuContext.Contact (the typed target of the plan commit) and the lists from the view model; they evaluate when the menu opens (no stale enable state). "Set as default" is greyed (no writer call yet, MENU-BOOKSET is M6). Owner con-ui.

using Barbican.Presentation.Menus;
using static Barbican.Presentation.Menus.MenuItems;

namespace Barbican.Presentation.Contacts;

/// <summary>The contacts menu providers.</summary>
public static class ContactsMenus
{
    /// <summary>How many providers <see cref="Create"/> returns.</summary>
    public const int Count = 8;

    public static IReadOnlyList<IMenuProvider> Create(IContactsViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(vm);
        return
        [
            new DelegateMenuProvider(ContactsMenuIds.Contact, c => Contact(c, vm)),
            new DelegateMenuProvider(ContactsMenuIds.ContactGroups, c => ContactGroups(c, vm)),
            new DelegateMenuProvider(ContactsMenuIds.ContactCategories, c => ContactCategories(c, vm)),
            new DelegateMenuProvider(ContactsMenuIds.Group, c => Group(c, vm)),
            new DelegateMenuProvider(ContactsMenuIds.GroupSend, GroupSend),
            new DelegateMenuProvider(ContactsMenuIds.Category, Category),
            new DelegateMenuProvider(ContactsMenuIds.Book, c => Book(c, vm)),
            new DelegateMenuProvider(ContactsMenuIds.New, _ => New()),
        ];
    }

    /// <summary>MENU-CONTACT: one contact, or the selection (then only what applies to several).</summary>
    public static IReadOnlyList<MenuItemSpec> Contact(MenuContext c, IContactsViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(vm);
        if (c.Contact is not { ContactIds.Count: > 0 } target)
        {
            return [];
        }

        var ids = target.ContactIds;
        var arg = ContactsArg.Join(ids.ToArray());
        var one = ids.Count == 1;
        var hasEmail = ids.Select(vm.CardOf).Any(card => card is not null && ContactsJson.Emails(card).Any());
        var items = new List<MenuItemSpec>();
        if (one)
        {
            items.Add(Item(MenuGlyphs.Open, "Open", ContactsCommandIds.Open, argument: arg));
        }

        items.Add(Item(MenuGlyphs.Reply, "Send email", ContactsCommandIds.SendEmail, argument: arg, enabled: hasEmail));
        if (one)
        {
            items.Add(Item(MenuGlyphs.Rename, "Edit", ContactsCommandIds.Edit, argument: arg));
        }

        items.Add(Sep());
        items.Add(Sub(MenuGlyphs.Contact, "Add to group", ContactsMenuIds.ContactGroups));
        items.Add(Sub(MenuGlyphs.Tag, "Categories", ContactsMenuIds.ContactCategories));
        if (one)
        {
            items.Add(Sep());
            items.Add(Item(MenuGlyphs.Copy, "Copy email address", ContactsCommandIds.CopyEmail, argument: arg, enabled: hasEmail));
        }

        items.Add(Sep());
        items.Add(Danger(MenuGlyphs.Delete, "Delete", ContactsCommandIds.Delete, hint: "Del", argument: arg));
        return items;
    }

    /// <summary>The "Add to group" flyout: every group (checked when all the contacts are members), then New group.</summary>
    public static IReadOnlyList<MenuItemSpec> ContactGroups(MenuContext c, IContactsViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(vm);
        if (c.Contact is not { ContactIds.Count: > 0 } target)
        {
            return [];
        }

        var cards = target.ContactIds.Select(vm.CardOf).Where(card => card is not null).Select(card => card!).ToList();
        var items = vm.GroupCards
            .OrderBy(g => ContactsListRules.Fold(ContactsListRules.FullName(g)), StringComparer.Ordinal)
            .Select(g => Item(MenuGlyphs.Contact, ContactsListRules.FullName(g), ContactsCommandIds.GroupToggle, argument: ContactsArg.Join(ContactsJson.Id(g), target.ContactIds), check: cards.Count > 0 && cards.All(card => ContactsJson.HasMember(g, card))))
            .ToList();
        if (items.Count > 0)
        {
            items.Add(Sep());
        }

        items.Add(Item(MenuGlyphs.Add, "New group…", ContactsCommandIds.GroupNewWith, argument: ContactsArg.Join(target.ContactIds.ToArray())));
        return items;
    }

    /// <summary>The "Categories" flyout: every category (checked when all the contacts have it), then New category.</summary>
    public static IReadOnlyList<MenuItemSpec> ContactCategories(MenuContext c, IContactsViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(vm);
        if (c.Contact is not { ContactIds.Count: > 0 } target)
        {
            return [];
        }

        var cards = target.ContactIds.Select(vm.CardOf).Where(card => card is not null).Select(card => card!).ToList();
        var items = vm.Categories
            .Select(cat => Item(MenuGlyphs.Tag, cat.Name, ContactsCommandIds.CategoryToggle, argument: ContactsArg.Join(cat.Name, target.ContactIds), check: cards.Count > 0 && cards.All(card => ContactsJson.Keys(card, "keywords").Contains(cat.Name, StringComparer.Ordinal))))
            .ToList();
        if (items.Count > 0)
        {
            items.Add(Sep());
        }

        items.Add(Item(MenuGlyphs.Add, "New category…", ContactsCommandIds.CategoryNew, argument: ContactsArg.Join(target.ContactIds.ToArray())));
        return items;
    }

    /// <summary>MENU-GROUP: a group of the list or the sidebar.</summary>
    public static IReadOnlyList<MenuItemSpec> Group(MenuContext c, IContactsViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(vm);
        if (c.Contact?.GroupId is not { } id || vm.CardOf(id) is not { } group)
        {
            return [];
        }

        return
        [
            Header(ContactsListRules.FullName(group)),
            Item(MenuGlyphs.Open, "Open", ContactsCommandIds.GroupShow, argument: id),
            Item(MenuGlyphs.Rename, "Rename group", ContactsCommandIds.GroupRename, argument: id),
            Item(MenuGlyphs.Contact, "Edit members", ContactsCommandIds.GroupEdit, argument: id),
            Sub(MenuGlyphs.Reply, "Send email to group", ContactsMenuIds.GroupSend),
            Sep(),
            Danger(MenuGlyphs.Delete, "Delete group", ContactsCommandIds.GroupDelete, argument: id),
        ];
    }

    /// <summary>The To, Cc and Bcc flyout of MENU-GROUP.</summary>
    public static IReadOnlyList<MenuItemSpec> GroupSend(MenuContext c)
    {
        ArgumentNullException.ThrowIfNull(c);
        if (c.Contact?.GroupId is not { } id)
        {
            return [];
        }

        return
        [
            Item(MenuGlyphs.Reply, "To", ContactsCommandIds.GroupSend, argument: ContactsArg.Join("to", id)),
            Item(MenuGlyphs.Reply, "Cc", ContactsCommandIds.GroupSend, argument: ContactsArg.Join("cc", id)),
            Item(MenuGlyphs.Reply, "Bcc", ContactsCommandIds.GroupSend, argument: ContactsArg.Join("bcc", id)),
        ];
    }

    /// <summary>MENU-CATEGORY: a category of the sidebar.</summary>
    public static IReadOnlyList<MenuItemSpec> Category(MenuContext c)
    {
        ArgumentNullException.ThrowIfNull(c);
        if (c.Contact?.Category is not { Length: > 0 } category)
        {
            return [];
        }

        return
        [
            Header(category),
            Item(MenuGlyphs.Open, "Show contacts", ContactsCommandIds.CategoryShow, argument: category),
            Item(MenuGlyphs.Rename, "Rename category", ContactsCommandIds.CategoryRename, argument: category),
            Sep(),
            Danger(MenuGlyphs.Delete, "Delete category", ContactsCommandIds.CategoryDelete, argument: category),
        ];
    }

    /// <summary>MENU-BOOK: an address book of the sidebar.</summary>
    public static IReadOnlyList<MenuItemSpec> Book(MenuContext c, IContactsViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(vm);
        if (c.Contact?.BookId is not { } id || vm.Books.FirstOrDefault(b => string.Equals(b.Id, id, StringComparison.Ordinal)) is not { } book)
        {
            return [];
        }

        return
        [
            Header(book.Name),
            Item(MenuGlyphs.Add, "New contact in this address book", ContactsCommandIds.BookNewContact, argument: id),
            Item(MenuGlyphs.Rename, "Rename", ContactsCommandIds.BookRename, argument: id),
            Item(MenuGlyphs.Default, "Set as default", ContactsCommandIds.BookRename, enabled: false),
            Sep(),
            Danger(MenuGlyphs.Delete, "Delete address book", ContactsCommandIds.BookDelete, argument: id, enabled: !book.IsDefault),
        ];
    }

    /// <summary>MENU-CONTACTS-NEW: the + button of the sidebar header (Bulwark contacts-sidebar.tsx:292-317): new contact, new group, new address book, a separator, import.</summary>
    public static IReadOnlyList<MenuItemSpec> New() =>
    [
        Item(MenuGlyphs.Contact, "New contact", ContactsCommandIds.New),
        Item(MenuGlyphs.Contact, "New group", ContactsCommandIds.NewGroup),
        Item(MenuGlyphs.Folder, "New address book", ContactsCommandIds.BookNew),
        Sep(),
        Item(MenuGlyphs.Upload, "Import contacts", ContactsCommandIds.Import),
    ];
}
