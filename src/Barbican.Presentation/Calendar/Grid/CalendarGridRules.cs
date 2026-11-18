// Mirrors: Rust calendar.rs:18 HOUR_PX, :202 day_columns, :146 week_days_shown, :189 week_start, :598 covers; Bulwark components/calendar/calendar-week-view.tsx:343-347 (DEFAULT_ALL_DAY_MAX_ROWS = 3, allDayHeight = max(28, rows * 24 + 4), expandable); B-CAL-008, 009; G-003 (the all-day strip is capped).
// Contract from the M4 plan section 4.5; the all-day constants are Bulwark's, the rest is cal-views'.

using Barbican.Core.Calendar;

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

    /// <summary>The day columns of the visible range [from, to): 3 days, 7 days or one day; each day from <paramref name="from"/> (at least one).</summary>
    public static IReadOnlyList<DateOnly> DayColumns(DateOnly from, DateOnly to)
    {
        var count = Math.Max(1, to.DayNumber - from.DayNumber);
        var days = new DateOnly[count];
        for (var i = 0; i < count; i++)
        {
            days[i] = from.AddDays(i);
        }

        return days;
    }

    /// <summary>The first day of the week containing <paramref name="day"/> (0 Sunday, 1 Monday, 6 Saturday).</summary>
    public static DateOnly WeekStart(DateOnly day, int firstDayOfWeek)
    {
        var offset = ((((int)day.DayOfWeek - firstDayOfWeek) % 7) + 7) % 7;
        return day.AddDays(-offset);
    }

    /// <summary>ISO week number of the day (the mini month and the Day title show it).</summary>
    public static int WeekNumber(DateOnly day) => System.Globalization.ISOWeek.GetWeekOfYear(day.ToDateTime(TimeOnly.MinValue));

    /// <summary>The first day of the month's six-week grid.</summary>
    public static DateOnly MonthGridStart(DateOnly anchor, int firstDayOfWeek) => WeekStart(new DateOnly(anchor.Year, anchor.Month, 1), firstDayOfWeek);

    /// <summary>Whether the occurrence shows on <paramref name="day"/>: an all-day or multi-day one on every day it covers (the end is exclusive), a point event on its day.</summary>
    public static bool Covers(CalendarOccurrence occurrence, DateOnly day)
    {
        ArgumentNullException.ThrowIfNull(occurrence);
        var last = occurrence.End > occurrence.Start ? DateOnly.FromDateTime(occurrence.End.AddTicks(-1)) : DateOnly.FromDateTime(occurrence.Start);
        return DateOnly.FromDateTime(occurrence.Start) <= day && last >= day;
    }

    /// <summary>An event drawn in the all-day strip: all-day, or timed and at least a day long (Rust, Bulwark).</summary>
    public static bool InAllDayStrip(CalendarOccurrence occurrence)
    {
        ArgumentNullException.ThrowIfNull(occurrence);
        return occurrence.AllDay || (occurrence.End - occurrence.Start).TotalHours >= 24;
    }
}
