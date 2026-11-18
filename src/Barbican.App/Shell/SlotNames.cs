// Mirrors: M2 plan section 3.3 (the slots of the shared XAML: SearchSlot and SelectionBarSlot of MessageListView; AccountHeaderSlot, UnifiedSectionSlot and SharedSectionSlot of SidebarView). Stage F of M2.

namespace Barbican.App.Shell;

/// <summary>The names of the slots in shared views.</summary>
public static class SlotNames
{
    /// <summary>The search box in the list header (search owns the content).</summary>
    public const string ListSearch = "list.search";

    /// <summary>Above the rows: the selection bar (actions).</summary>
    public const string ListSelectionBar = "list.selection-bar";

    /// <summary>The account header at the top of the sidebar (menus, I-037).</summary>
    public const string SidebarAccountHeader = "sidebar.account-header";

    /// <summary>The unified inbox section of the sidebar (unified, wave 2).</summary>
    public const string SidebarUnified = "sidebar.unified";

    /// <summary>The shared accounts section of the sidebar (unified, wave 2).</summary>
    public const string SidebarShared = "sidebar.shared";

    /// <summary>The Tasks view in the calendar surface (M4: cal-ui fills it, cal-views hosts it).</summary>
    public const string CalendarTasks = "calendar.tasks";

    /// <summary>The capped task list under the mini month in the calendar sidebar (M4: cal-ui fills it).</summary>
    public const string CalendarSidebarTasks = "calendar.sidebar.tasks";
}
