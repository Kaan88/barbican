// Mirrors: spec/menus.md MENU-EVENT, MENU-SLOT, MENU-TASK, MENU-CAL (Rust ctxmenu.rs:249-294); M4 plan section 4.5 (cal-views raises the menus through IContextMenuService, cal-ui provides the items). Additive contract of cal-views: MenuContext.Calendar.

namespace Barbican.Presentation.Calendar.Grid;

/// <summary>The ids of the calendar menus (spec/menus.md); cal-ui registers an <c>IMenuProvider</c> for each.</summary>
public static class CalendarMenuIds
{
    public const string Event = "MENU-EVENT";
    public const string Slot = "MENU-SLOT";
    public const string Task = "MENU-TASK";
    public const string Calendar = "MENU-CAL";
}
