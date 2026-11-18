// Mirrors: Rust calendar.rs ST.view (0 Day, 1 Week, 2 Month, 3 Agenda, 4 Tasks); Bulwark components/calendar/calendar-toolbar.tsx:484; I-311, B-CAL-010. Contract from the M4 plan section 4.5; owner cal-views.

namespace Barbican.Presentation.Calendar.Grid;

/// <summary>The views of the calendar. The numbers are what <c>ShortcutAction.CalView</c> carries (d 0, w 1, m 2; a 3 and k 4 are new).</summary>
public enum CalendarViewKind
{
    Day = 0,
    Week = 1,
    Month = 2,
    Agenda = 3,
    Tasks = 4,
}
