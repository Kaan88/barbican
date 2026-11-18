// Mirrors: Rust calendar.rs:376-:389 (a task due in the range is an all-day chip on its due day, title "<marker> title"), :746 is_task; Bulwark lib/calendar-tasks.ts isTaskObject, components/calendar/task-chip.tsx; I-320.
// The due day is the date part of the due value (a timed due date in another zone is not converted here: the chip is a day marker).

using System.Text.Json.Nodes;
using Barbican.Core.Calendar;

namespace Barbican.Presentation.Calendar.Grid;

/// <summary>Tasks as occurrences on the grid.</summary>
public static class TaskOccurrences
{
    /// <summary>The done marker (U+2713) and the open marker (U+25CB) the grid titles carry.</summary>
    public const string DoneMarker = "✓";

    public const string OpenMarker = "○";

    /// <summary>A JSCalendar Task (RFC 8984 2.2).</summary>
    public static bool IsTask(JsonNode ev)
    {
        ArgumentNullException.ThrowIfNull(ev);
        return ev["@type"] is JsonValue v && v.TryGetValue<string>(out var t) && string.Equals(t, "Task", StringComparison.Ordinal);
    }

    /// <summary>The tasks of <paramref name="events"/> whose due day is in [from, to), in due order.</summary>
    public static IReadOnlyList<CalendarOccurrence> In(IReadOnlyList<JsonNode> events, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(events);
        var result = new List<CalendarOccurrence>();
        foreach (var e in events)
        {
            if (!IsTask(e) || e["due"] is not JsonValue due || !due.TryGetValue<string>(out var text) || TaskDayFilter.DueDate(text) is not { } day || day < from || day >= to)
            {
                continue;
            }

            var done = e["progress"] is JsonValue p && p.TryGetValue<string>(out var progress) && string.Equals(progress, "completed", StringComparison.Ordinal);
            var title = e["title"] is JsonValue t && t.TryGetValue<string>(out var name) && name.Length > 0 ? name : "(untitled task)";
            var id = e["id"] is JsonValue i && i.TryGetValue<string>(out var s) ? s : string.Empty;
            var start = day.ToDateTime(TimeOnly.MinValue);
            result.Add(new CalendarOccurrence("task:" + id, id, null, (done ? DoneMarker : OpenMarker) + " " + title, start, start.AddDays(1), true, CalendarRows.FirstCalendar(e), string.Empty, CalendarOccurrenceKind.Task, done));
        }

        result.Sort((a, b) => a.Start.CompareTo(b.Start));
        return result;
    }
}
