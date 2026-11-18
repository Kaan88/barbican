// Mirrors: Rust calendar.rs:493-:512 (the 42 cells, 4 events then "+N more", time only with showTimeInMonthView, calendarMonthTimedEvents styles); Bulwark components/calendar/calendar-month-view.tsx. B-CAL-055; I-316, I-319.

namespace Barbican.Presentation.Calendar.Grid;

/// <summary>How a timed event looks in a month cell (calendarMonthTimedEvents: bar, text, dot).</summary>
public enum MonthItemStyle
{
    Bar = 0,
    Text = 1,
    Dot = 2,
}
