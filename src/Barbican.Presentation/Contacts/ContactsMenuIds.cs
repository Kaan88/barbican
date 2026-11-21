// Mirrors: spec/menus.md MENU-CONTACT, MENU-GROUP, MENU-CATEGORY, MENU-BOOK; M5 plan section 3.3 (con-ui owns ContactsMenuIds). Owner con-ui.

namespace Barbican.Presentation.Contacts;

/// <summary>The ids of the contacts menus and their flyouts.</summary>
public static class ContactsMenuIds
{
    public const string Contact = "MENU-CONTACT";
    public const string Group = "MENU-GROUP";
    public const string Category = "MENU-CATEGORY";
    public const string Book = "MENU-BOOK";
    public const string New = "MENU-CONTACTS-NEW";
    public const string ContactGroups = "MENU-CONTACT>groups";
    public const string ContactCategories = "MENU-CONTACT>categories";
    public const string GroupSend = "MENU-GROUP>send";
}
