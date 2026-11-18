// Mirrors: Rust calendar.rs:493-:512 (the 42 cells, 4 events then "+N more", time only with showTimeInMonthView, calendarMonthTimedEvents styles); Bulwark components/calendar/calendar-month-view.tsx. B-CAL-055; I-316, I-319.

namespace Barbican.Presentation.Calendar.Grid;

/// <summary>One day of the month grid; <c>More</c> is the number of items that did not fit ("+N more").</summary>
public sealed record MonthCell(DateOnly Day, bool InMonth, bool Today, IReadOnlyList<MonthItem> Items, int More);
