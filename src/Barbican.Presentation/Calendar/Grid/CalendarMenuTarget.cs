// Mirrors: spec/menus.md MENU-EVENT, MENU-SLOT, MENU-TASK, MENU-CAL (Rust ctxmenu.rs:249-294); M4 plan section 4.5 (cal-views raises the menus through IContextMenuService, cal-ui provides the items). Additive contract of cal-views: MenuContext.Calendar.

namespace Barbican.Presentation.Calendar.Grid;

/// <summary>
/// What a calendar menu was opened on: an occurrence (<c>OccurrenceId</c>, "id", "id|recurrenceId" or "task:ID"), an empty slot (<c>Slot</c> is the clicked time, <c>AllDay</c> when it is a day cell or the all-day strip)
/// or a calendar of the sidebar (<c>CalendarId</c>). <c>AccountKey</c> is the login.
/// </summary>
public sealed record CalendarMenuTarget(string AccountKey, string? OccurrenceId = null, DateTime? Slot = null, bool AllDay = false, string? CalendarId = null);
