// Mirrors: Rust calendar.rs:493-:512 (the 42 cells, 4 events then "+N more", time only with showTimeInMonthView, calendarMonthTimedEvents styles); Bulwark components/calendar/calendar-month-view.tsx. B-CAL-055; I-316, I-319.

using Barbican.Core.Calendar;

namespace Barbican.Presentation.Calendar.Grid;

/// <summary>The month grid: 42 cells, pure.</summary>
public static class MonthLayout
{
    /// <summary>The items shown in a cell before "+N more".</summary>
    public const int MaxItems = 4;

    /// <summary>The six-week grid from <paramref name="gridStart"/>; each cell lists the occurrences that cover its day (one pass over the occurrences).</summary>
    public static IReadOnlyList<MonthCell> Cells(DateOnly gridStart, DateOnly anchor, DateOnly today, IReadOnlyList<CalendarOccurrence> occurrences, CalendarViewSettings settings, int maxItems = MaxItems)
    {
        ArgumentNullException.ThrowIfNull(occurrences);
        ArgumentNullException.ThrowIfNull(settings);
        var byDay = new List<CalendarOccurrence>?[42];
        foreach (var o in occurrences)
        {
            var first = DateOnly.FromDateTime(o.Start);
            var last = o.End > o.Start ? DateOnly.FromDateTime(o.End.AddTicks(-1)) : first;
            var from = Math.Max(first.DayNumber - gridStart.DayNumber, 0);
            var to = Math.Min(last.DayNumber - gridStart.DayNumber, 41);
            for (var i = from; i <= to; i++)
            {
                (byDay[i] ??= []).Add(o);
            }
        }

        var cells = new List<MonthCell>(42);
        for (var i = 0; i < 42; i++)
        {
            var day = gridStart.AddDays(i);
            var list = byDay[i];
            if (list is null)
            {
                cells.Add(new MonthCell(day, day.Month == anchor.Month && day.Year == anchor.Year, day == today, [], 0));
                continue;
            }

            list.Sort((a, b) => a.AllDay != b.AllDay ? (a.AllDay ? -1 : 1) : a.Start.CompareTo(b.Start));
            var items = new List<MonthItem>(Math.Min(list.Count, maxItems));
            for (var k = 0; k < list.Count && k < maxItems; k++)
            {
                items.Add(Item(list[k], day, settings));
            }

            cells.Add(new MonthCell(day, day.Month == anchor.Month && day.Year == anchor.Year, day == today, items, Math.Max(0, list.Count - maxItems)));
        }

        return cells;
    }

    private static MonthItem Item(CalendarOccurrence o, DateOnly day, CalendarViewSettings settings)
    {
        // Bulwark: only the start time, and only with showTimeInMonthView; all-day and multi-day events stay bars
        var time = settings.Core.ShowTimeInMonthView && !o.AllDay && DateOnly.FromDateTime(o.Start) == day ? CalendarFormat.Time(o.Start, settings.Core.TwelveHour) : string.Empty;
        var timed = !o.AllDay && (o.End - o.Start).TotalHours < 24;
        return new MonthItem(o, time, timed ? settings.MonthStyle : MonthItemStyle.Bar);
    }
}
