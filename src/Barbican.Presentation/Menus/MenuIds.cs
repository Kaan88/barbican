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
    public const string ReaderSelection = "MENU-SELECTION";
    public const string Folder = "MENU-FOLDER";
    public const string Tag = "MENU-TAG";
    public const string List = "MENU-LIST";
    public const string ListActions = "MENU-LIST-ACTIONS";
    public const string Account = "ACCOUNT";
    public const string FoldersHeader = "FOLDERS-HEADER";
    public const string UnifiedRow = "UNIFIED-ROW";

    // The calendar menus (M4, cal-ui: spec/menus.md MENU-EVENT, MENU-SLOT, MENU-TASK, MENU-CAL).
    public const string Event = "MENU-EVENT";

    public const string Slot = "MENU-SLOT";

    public const string Task = "MENU-TASK";

    public const string Calendar = "MENU-CAL";

    // Flyouts of the menus above: a Submenu row names one; its provider is registered under this id.
    public const string MailMove = "MENU-MAIL>move";
    public const string MailTag = "MENU-MAIL>tag";
    public const string MailMore = "MENU-MAIL>more";
    public const string TagColour = "MENU-TAG>colour";
    public const string ListSort = "MENU-LIST-ACTIONS>sort";

    public const string TaskDue = "MENU-TASK>due";

    public const string CalendarColour = "MENU-CAL>colour";
}
