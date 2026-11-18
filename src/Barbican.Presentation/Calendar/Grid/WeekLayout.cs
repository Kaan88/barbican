// Mirrors: Rust calendar.rs:526-:566 (timed events with overlap lanes, all-day heads), :18 HOUR_PX; Bulwark components/calendar/calendar-week-view.tsx (overlap groups, the all-day strip rows). B-CAL-009; G-003.

using Barbican.Core.Calendar;

namespace Barbican.Presentation.Calendar.Grid;

/// <summary>Where the events of the Day and Week grids go: pure, tested, no controls.</summary>
public static class WeekLayout
{
    /// <summary>The shortest a timed card is drawn, in minutes (Rust: 20).</summary>
    public const int MinMinutes = 20;

    /// <summary>
    /// The timed events of each column, clipped to the day (an overnight one continues on the next column), side by side in lanes inside each overlap group (a group is events
    /// that touch each other, so a lone event next to a crowded hour keeps its full width). Events wholly outside [hourFrom, hourTo) are left out.
    /// </summary>
    public static IReadOnlyList<TimedBox> Timed(IReadOnlyList<DateOnly> columns, IReadOnlyList<CalendarOccurrence> occurrences, int hourFrom, int hourTo)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(occurrences);
        var result = new List<TimedBox>();
        var timed = occurrences.Where(o => !CalendarGridRules.InAllDayStrip(o)).ToList();
        for (var c = 0; c < columns.Count; c++)
        {
            var dayStart = columns[c].ToDateTime(TimeOnly.MinValue);
            var dayEnd = dayStart.AddDays(1);
            var segments = new List<(CalendarOccurrence Occ, DateTime Start, DateTime End)>();
            foreach (var o in timed)
            {
                if (o.Start >= dayEnd || (o.End <= dayStart && !(o.End == o.Start && o.Start >= dayStart)))
                {
                    continue;
                }

                var s = o.Start < dayStart ? dayStart : o.Start;
                var e = o.End > dayEnd ? dayEnd : o.End;
                var minEnd = s.AddMinutes(MinMinutes);
                if (e < minEnd)
                {
                    e = minEnd > dayEnd ? dayEnd : minEnd;
                }

                if (e <= dayStart.AddHours(hourFrom) || s >= dayStart.AddHours(hourTo))
                {
                    continue;
                }

                segments.Add((o, s, e));
            }

            segments.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : b.End.CompareTo(a.End));
            PlaceGroup(segments, c, dayStart, hourFrom, result);
        }

        return result;
    }

    /// <summary>The narrowest a lane may be before the lanes of an overlap group cascade instead of sharing the column (px; a card narrower than this cannot show a title).</summary>
    public const double MinLaneWidth = 64;

    /// <summary>The widest step between cascaded lanes (px).</summary>
    public const double CascadeStep = 22;

    /// <summary>
    /// Where lane <paramref name="lane"/> of <paramref name="lanes"/> goes in a column <paramref name="columnWidth"/> px wide, as left offset and width: side by side while each lane keeps <see cref="MinLaneWidth"/>,
    /// else cascaded (each later lane starts a step to the right and runs to the column's edge, drawn over the earlier ones) so every title stays readable (D-M5-I05).
    /// </summary>
    public static (double Left, double Width) Slot(int lane, int lanes, double columnWidth)
    {
        if (lanes <= 1)
        {
            return (0, columnWidth);
        }

        if (columnWidth / lanes >= MinLaneWidth)
        {
            var w = columnWidth / lanes;
            return (lane * w, w);
        }

        var step = Math.Min(CascadeStep, Math.Max(8, (columnWidth - MinLaneWidth) / (lanes - 1)));
        var left = lane * step;
        return (left, Math.Max(MinLaneWidth, columnWidth - left));
    }

    private static void PlaceGroup(List<(CalendarOccurrence Occ, DateTime Start, DateTime End)> segments, int column, DateTime dayStart, int hourFrom, List<TimedBox> result)
    {
        var i = 0;
        while (i < segments.Count)
        {
            var lanes = new List<DateTime>();
            var group = new List<(CalendarOccurrence Occ, DateTime Start, DateTime End, int Lane)>();
            var groupEnd = segments[i].End;
            for (; i < segments.Count && segments[i].Start < groupEnd; i++)
            {
                var seg = segments[i];
                var lane = lanes.FindIndex(end => end <= seg.Start);
                if (lane < 0)
                {
                    lanes.Add(seg.End);
                    lane = lanes.Count - 1;
                }
                else
                {
                    lanes[lane] = seg.End;
                }

                if (seg.End > groupEnd)
                {
                    groupEnd = seg.End;
                }

                group.Add((seg.Occ, seg.Start, seg.End, lane));
            }

            foreach (var g in group)
            {
                var top = ((g.Start - dayStart).TotalMinutes / 60.0 * CalendarGridRules.HourPx) - (hourFrom * CalendarGridRules.HourPx);
                var height = (g.End - g.Start).TotalMinutes / 60.0 * CalendarGridRules.HourPx;
                if (top < 0)
                {
                    height += top;
                    top = 0;
                }

                result.Add(new TimedBox(g.Occ, column, top, Math.Max(height, 1), g.Lane, lanes.Count));
            }
        }
    }

    /// <summary>
    /// The all-day strip: each event once, from its first to its last visible column (a column may be missing when non-working days are hidden), stacked into the first free row.
    /// <c>Rows</c> is the number of rows the content needs; the view caps it (<see cref="CalendarGridRules.AllDayVisibleRows"/>).
    /// </summary>
    public static (IReadOnlyList<AllDayBar> Bars, int Rows) AllDay(IReadOnlyList<DateOnly> columns, IReadOnlyList<CalendarOccurrence> occurrences)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(occurrences);
        var spans = new List<(CalendarOccurrence Occ, int First, int Last)>();
        foreach (var o in occurrences.Where(CalendarGridRules.InAllDayStrip))
        {
            var first = -1;
            var last = -1;
            for (var c = 0; c < columns.Count; c++)
            {
                if (CalendarGridRules.Covers(o, columns[c]))
                {
                    first = first < 0 ? c : first;
                    last = c;
                }
            }

            if (first >= 0)
            {
                spans.Add((o, first, last));
            }
        }

        spans.Sort((a, b) =>
        {
            var c = a.First.CompareTo(b.First);
            if (c != 0)
            {
                return c;
            }

            c = (b.Last - b.First).CompareTo(a.Last - a.First);
            return c != 0 ? c : string.Compare(a.Occ.Title, b.Occ.Title, StringComparison.OrdinalIgnoreCase);
        });
        var rowEnds = new List<int>();
        var bars = new List<AllDayBar>();
        foreach (var s in spans)
        {
            var row = rowEnds.FindIndex(end => end < s.First);
            if (row < 0)
            {
                rowEnds.Add(s.Last);
                row = rowEnds.Count - 1;
            }
            else
            {
                rowEnds[row] = s.Last;
            }

            bars.Add(new AllDayBar(s.Occ, s.First, s.Last - s.First + 1, row));
        }

        return (bars, rowEnds.Count);
    }
}
