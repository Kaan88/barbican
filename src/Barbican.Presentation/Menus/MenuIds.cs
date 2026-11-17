// Mirrors: M2 plan section 4.2 (MenuIds); spec/menus.md (MENU-* sections), Bulwark components/layout/account-switcher.tsx, sidebar.tsx:2074 and :2867. Contract from stage F of M2.
// Providers: menus provides MAIL, THREAD, ADDR, BODY, FOLDER, TAG, LIST, ACCOUNT, FOLDERS-HEADER; reader provides READER and ATT; unified provides UNIFIED-ROW.

namespace Barbican.Presentation.Menus;

/// <summary>The ids of the menus (spec/menus.md names).</summary>
public static class MenuIds
{
    public const string Mail = "MENU-MAIL";
    public const string Thread = "MENU-THREAD";
    public const string Address = "MENU-ADDR";
    public const string Attachment = "MENU-ATT";
    public const string Reader = "MENU-READER";
    public const string Body = "MENU-BODY";
    public const string Folder = "MENU-FOLDER";
    public const string Tag = "MENU-TAG";
    public const string List = "MENU-LIST";
    public const string Account = "ACCOUNT";
    public const string FoldersHeader = "FOLDERS-HEADER";
    public const string UnifiedRow = "UNIFIED-ROW";
}
