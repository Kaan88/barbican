// Mirrors: Rust calendar.rs:493-:512 (the 42 cells, 4 events then "+N more", time only with showTimeInMonthView, calendarMonthTimedEvents styles); Bulwark components/calendar/calendar-month-view.tsx. B-CAL-055; I-316, I-319.

using Barbican.Core.Calendar;

namespace Barbican.Presentation.Calendar.Grid;

/// <summary>One line of a month cell. <c>Time</c> is empty unless the time is shown.</summary>
public sealed record MonthItem(CalendarOccurrence Occurrence, string Time, MonthItemStyle Style);
