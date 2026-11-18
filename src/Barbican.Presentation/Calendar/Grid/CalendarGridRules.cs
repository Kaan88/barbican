// Mirrors: Rust calendar.rs:18 HOUR_PX, :202 day_columns, :146 week_days_shown; Bulwark components/calendar/calendar-week-view.tsx:343-347 (DEFAULT_ALL_DAY_MAX_ROWS = 3, allDayHeight = max(28, rows * 24 + 4), expandable); B-CAL-008, 009; G-003 (the all-day strip is capped).
// Contract from the M4 plan section 4.5; owner cal-views replaces the throwing member; the all-day constants are real (Bulwark's).

using Barbican.Core.Common;

namespace Barbican.Presentation.Calendar.Grid;

/// <summary>Pure rules of the day and week grids.</summary>
public static class CalendarGridRules
{
    /// <summary>Hour rows are 56 px tall (B-CAL-009).</summary>
    public const double HourPx = 56;

    /// <summary>The all-day strip shows at most this many rows until it is expanded (G-003).</summary>
    public const int AllDayDefaultMaxRows = 3;

    /// <summary>The rows the strip shows: all of them when expanded, else at most <see cref="AllDayDefaultMaxRows"/>.</summary>
    public static int AllDayVisibleRows(int contentRows, bool expanded) => expanded ? contentRows : Math.Min(AllDayDefaultMaxRows, contentRows);

    /// <summary>The strip's height in px for the rows shown: at least 28, else 24 per row plus 4.</summary>
    public static double AllDayHeight(int visibleRows) => Math.Max(28, (visibleRows * 24) + 4);

    /// <summary>Whether the strip has a show-more toggle (more content rows than the default cap).</summary>
    public static bool AllDayExpandable(int contentRows) => contentRows > AllDayDefaultMaxRows;

    /// <summary>The day columns of the visible range [from, to): 3 days, 7 days or one day; each day from <paramref name="from"/>.</summary>
    public static IReadOnlyList<DateOnly> DayColumns(DateOnly from, DateOnly to) => throw new PortPendingException("CalendarGridRules.DayColumns");
}
