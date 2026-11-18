// Mirrors: Bulwark components/calendar/calendar-print.tsx + lib/calendar-print.ts (the visible view as a printable page); G-008, I-312, I-341; decision D-M4-14 (an in-app print document, not the browser).
// Pure: the model is pages of cells of lines. The preview draws it in the window; the OS print path (a button of the view) is never run by an agent or a test.

using Barbican.Core.Calendar;

namespace Barbican.Presentation.Calendar.Grid;

/// <summary>Builds the print document of the visible range.</summary>
public static class CalendarPrintModel
{
    /// <summary>Agenda lines per page.</summary>
    public const int AgendaLinesPerPage = 38;

    public static PrintDocument Build(CalendarViewKind view, DateOnly anchor, (DateOnly From, DateOnly To) range, IReadOnlyList<CalendarOccurrence> occurrences, IReadOnlyList<CalendarListRow> calendars, CalendarViewSettings settings, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(occurrences);
        ArgumentNullException.ThrowIfNull(calendars);
        ArgumentNullException.ThrowIfNull(settings);
        var title = CalendarFormat.Title(view, anchor, range.From, range.To, settings.Core.ShowWeekNumbers);
        var h12 = settings.Core.TwelveHour;
        var colours = calendars.ToDictionary(c => c.Id, c => c.Color, StringComparer.Ordinal);
        switch (view)
        {
            case CalendarViewKind.Day:
                return new PrintDocument(title, [new PrintPage(title, 1, [Cell(range.From, occurrences, colours, h12, today, false, wide: true)])]);
            case CalendarViewKind.Week:
                return new PrintDocument(title, [new PrintPage(title, 7, Days(range.From, range.To, occurrences, colours, h12, today, anchor, monthOnly: false))]);
            case CalendarViewKind.Agenda:
                return new PrintDocument(title, AgendaPages(title, range, occurrences, colours, h12, today));
            default:
                return new PrintDocument(title, [new PrintPage(title, 7, Days(range.From, range.To, occurrences, colours, h12, today, anchor, monthOnly: true))]);
        }
    }

    private static List<PrintCell> Days(DateOnly from, DateOnly to, IReadOnlyList<CalendarOccurrence> occurrences, Dictionary<string, string> colours, bool h12, DateOnly today, DateOnly anchor, bool monthOnly)
    {
        var cells = new List<PrintCell>();
        for (var d = from; d < to; d = d.AddDays(1))
        {
            var muted = monthOnly && (d.Month != anchor.Month || d.Year != anchor.Year);
            cells.Add(Cell(d, occurrences, colours, h12, today, muted, wide: false));
        }

        return cells;
    }

    private static PrintCell Cell(DateOnly day, IReadOnlyList<CalendarOccurrence> occurrences, Dictionary<string, string> colours, bool h12, DateOnly today, bool muted, bool wide)
    {
        var lines = occurrences
            .Where(o => CalendarGridRules.Covers(o, day))
            .OrderBy(o => !o.AllDay)
            .ThenBy(o => o.Start)
            .Select(o => new PrintLine(
                o.AllDay || (o.Start < day.ToDateTime(TimeOnly.MinValue)) ? string.Empty : CalendarFormat.Time(o.Start, h12),
                o.Kind == CalendarOccurrenceKind.Task ? AgendaRules.TaskTitle(o.Title) : o.Title,
                colours.TryGetValue(o.Calendar, out var c) ? c : "#3b82f6"))
            .ToList();
        return new PrintCell(wide ? day.ToString("dddd d MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture) : day.ToString("ddd d", System.Globalization.CultureInfo.InvariantCulture), muted, day == today, lines);
    }

    private static List<PrintPage> AgendaPages(string title, (DateOnly From, DateOnly To) range, IReadOnlyList<CalendarOccurrence> occurrences, Dictionary<string, string> colours, bool h12, DateOnly today)
    {
        var pages = new List<PrintPage>();
        var cells = new List<PrintCell>();
        var budget = 0;
        for (var d = range.From; d < range.To; d = d.AddDays(1))
        {
            var cell = Cell(d, occurrences, colours, h12, today, false, wide: false);
            if (cell.Lines.Count == 0)
            {
                continue;
            }

            cell = cell with { Heading = CalendarFormat.DayHeading(d, today) };
            var cost = cell.Lines.Count + 1;
            if (budget + cost > AgendaLinesPerPage && cells.Count > 0)
            {
                pages.Add(new PrintPage(title, 1, cells));
                cells = [];
                budget = 0;
            }

            cells.Add(cell);
            budget += cost;
        }

        if (cells.Count > 0 || pages.Count == 0)
        {
            pages.Add(new PrintPage(title, 1, cells));
        }

        return pages;
    }
}
