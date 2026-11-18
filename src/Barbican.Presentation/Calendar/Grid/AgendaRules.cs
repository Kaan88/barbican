// Mirrors: Rust cal_extra.rs:138 agenda_time, :229 agenda_task_title, :149 render_agenda; Bulwark components/calendar/calendar-agenda-view.tsx (agendaDayPart); B-CAL-006, 037, 047, 048.
// B-CAL-048 (no state borrow held while rendering): the rows are built from the immutable list the view model handed out, never from a store that may change underneath.

using Barbican.Core.Calendar;

namespace Barbican.Presentation.Calendar.Grid;

/// <summary>Pure rules of the Agenda view.</summary>
public static class AgendaRules
{
    /// <summary>The title without the task marker the grid adds (the open circle U+25CB and the check U+2713), which the agenda draws itself.</summary>
    public static string TaskTitle(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        return title.Length > 0 && (title[0] == '✓' || title[0] == '○') ? title[1..].TrimStart() : title;
    }

    /// <summary>The time column for <paramref name="day"/>: "22:00 – 07:30" on the first day of an overnight event, "All day" on the days between, "until 07:30" on its last day; "All day" for an all-day event ("Due" for a task); 12-hour times when asked.</summary>
    public static string TimeLabel(CalendarOccurrence occurrence, DateOnly day, bool twelveHour)
    {
        ArgumentNullException.ThrowIfNull(occurrence);
        if (occurrence.AllDay)
        {
            return occurrence.Kind == CalendarOccurrenceKind.Task ? "Due" : "All day";
        }

        var dayStart = day.ToDateTime(TimeOnly.MinValue);
        if (occurrence.Start >= dayStart)
        {
            return CalendarFormat.Time(occurrence.Start, twelveHour) + " – " + CalendarFormat.Time(occurrence.End, twelveHour);
        }

        return occurrence.End >= dayStart.AddDays(1) ? "All day" : "until " + CalendarFormat.Time(occurrence.End, twelveHour);
    }

    /// <summary>
    /// The list of the Agenda over [from, to): per day the heading and the items, all-day first, then by when the item shows on that day; a day without items is left out except today
    /// (it says so). One pass over the occurrences, so thousands of tasks stay cheap (L-027).
    /// </summary>
    public static IReadOnlyList<AgendaRow> Rows(IReadOnlyList<CalendarOccurrence> occurrences, DateOnly from, DateOnly to, DateOnly today, bool twelveHour)
    {
        ArgumentNullException.ThrowIfNull(occurrences);
        var byDay = new Dictionary<DateOnly, List<CalendarOccurrence>>();
        foreach (var o in occurrences)
        {
            var first = DateOnly.FromDateTime(o.Start);
            var last = o.End > o.Start ? DateOnly.FromDateTime(o.End.AddTicks(-1)) : first;
            var d = first < from ? from : first;
            var end = last >= to ? to.AddDays(-1) : last;
            for (; d <= end; d = d.AddDays(1))
            {
                if (!byDay.TryGetValue(d, out var list))
                {
                    list = [];
                    byDay[d] = list;
                }

                list.Add(o);
            }
        }

        var rows = new List<AgendaRow>();
        for (var day = from; day < to; day = day.AddDays(1))
        {
            byDay.TryGetValue(day, out var items);
            var isToday = day == today;
            if (items is null && !isToday)
            {
                continue;
            }

            var heading = CalendarFormat.DayHeading(day, today);
            if (items is null)
            {
                rows.Add(new AgendaRow(heading, day, isToday, true, null, string.Empty, string.Empty, string.Empty, string.Empty, CalendarOccurrenceKind.Event, false));
                continue;
            }

            var dayStart = day.ToDateTime(TimeOnly.MinValue);
            items.Sort((a, b) => Compare(a, b, dayStart));
            var firstOfDay = true;
            foreach (var o in items)
            {
                var title = o.Kind == CalendarOccurrenceKind.Task ? TaskTitle(o.Title) : o.Title;
                rows.Add(new AgendaRow(firstOfDay ? heading : string.Empty, day, isToday, false, o.Id, title, TimeLabel(o, day, twelveHour), o.Location, o.Calendar, o.Kind, o.Done));
                firstOfDay = false;
            }
        }

        return rows;
    }

    private static int Compare(CalendarOccurrence a, CalendarOccurrence b, DateTime dayStart)
    {
        var sa = a.AllDay || a.Start < dayStart ? dayStart : a.Start;
        var sb = b.AllDay || b.Start < dayStart ? dayStart : b.Start;
        var c = sa.CompareTo(sb);
        if (c != 0)
        {
            return c;
        }

        c = b.AllDay.CompareTo(a.AllDay);
        return c != 0 ? c : string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
    }
}
