// Mirrors: M4 CalendarCommandIds (the ids the menus, the palette, the keys and the automation commands run); spec/menus.md MENU-CONTACT, MENU-GROUP, MENU-CATEGORY, MENU-BOOK; M5 plan section 6.2 step 3 (contacts.import, contacts.export, contacts.new). Owner con-ui.

namespace Barbican.Presentation.Contacts;

/// <summary>The command ids of the Contacts app.</summary>
public static class ContactsCommandIds
{
    public const string New = "contacts.new";
    public const string Edit = "contacts.edit";
    public const string Open = "contacts.open";
    public const string Delete = "contacts.delete";
    public const string SendEmail = "contacts.send-email";
    public const string CopyEmail = "contacts.copy-email";
    public const string GroupToggle = "contacts.group-toggle";
    public const string GroupNewWith = "contacts.group-new-with";
    public const string NewGroup = "contacts.new-group";
    public const string GroupEdit = "contacts.group-edit";
    public const string GroupRename = "contacts.group-rename";
    public const string GroupDelete = "contacts.group-delete";
    public const string GroupSend = "contacts.group-send";
    public const string CategoryToggle = "contacts.category-toggle";
    public const string CategoryNew = "contacts.category-new";
    public const string CategoryShow = "contacts.category-show";
    public const string CategoryRename = "contacts.category-rename";
    public const string CategoryDelete = "contacts.category-delete";
    public const string BookNew = "contacts.book-new";
    public const string BookNewContact = "contacts.book-new-contact";
    public const string BookRename = "contacts.book-rename";
    public const string BookDelete = "contacts.book-delete";
    public const string BookShow = "contacts.book-show";
    public const string GroupShow = "contacts.group-show";
    public const string Import = "contacts.import";
    public const string Export = "contacts.export";
    public const string Merge = "contacts.merge";
    public const string Print = "contacts.print";
    public const string ShowAll = "contacts.show-all";
    public const string ShowDuplicates = "contacts.show-duplicates";
    public const string ShowDirectory = "contacts.show-directory";
    public const string Refresh = "contacts.refresh";
    public const string KeyUp = "contacts.key-up";
    public const string KeyDown = "contacts.key-down";
    public const string KeyFirst = "contacts.key-first";
    public const string KeyLast = "contacts.key-last";
    public const string KeyClear = "contacts.key-clear";
    public const string KeySearch = "contacts.key-search";
    public const string KeyMenu = "contacts.key-menu";
}
