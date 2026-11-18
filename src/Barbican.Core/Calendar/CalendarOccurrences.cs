// Mirrors: Rust calendar.rs:355 occurrences, :373 occurrences_in, :598 covers, :746 is_task; B-CAL-004/005 (recurrence expanded on the client, tasks not expanded as events), B-CAL-006 (agenda 30 days).
// Contract from the M4 plan section 4.2; owner cal-data.

using System.Globalization;
using System.Text.Json.Nodes;
using Barbican.Core.Calendar.Recurrence;

namespace Barbican.Core.Calendar;

/// <summary>Events to grid occurrences, on the user's clock.</summary>
public static class CalendarOccurrences
{
    /// <summary>The agenda covers 30 days from its start day (B-CAL-006).</summary>
    public const int AgendaDays = 30;

    /// <summary>The occurrences of the events (not tasks) overlapping [from, to) on the user's clock: ids "id" or "id|recurrenceId", an empty title "(no title)", the event's own zone converted. <paramref name="local"/> is the user's zone (default: the machine's).</summary>
    public static IReadOnlyList<CalendarOccurrence> In(IReadOnlyList<JsonNode> events, DateOnly from, DateOnly to, TimeZoneInfo? local = null)
    {
        ArgumentNullException.ThrowIfNull(events);
        var f = from.ToDateTime(TimeOnly.MinValue);
        var t = to.ToDateTime(TimeOnly.MinValue);
        var occ = new List<CalendarOccurrence>();
        foreach (var e in events)
        {
            if (IsTask(e))
            {
                continue;
            }

            var allDay = e.IsTrue("showWithoutTime");
            var zone = allDay ? null : e.Str("timeZone") is { Length: > 0 } z ? z : null;

            // the window on the event's own clock, a day wider for zones far from the user's
            var zf = TimeZones.LocalToZone(f, zone, local).AddDays(-1);
            var zt = TimeZones.LocalToZone(t, zone, local).AddDays(1);
            var id = e.Str("id") ?? string.Empty;
            var title = e.Str("title") is { Length: > 0 } tt ? tt : "(no title)";
            var location = LocationOf(e);
            var calendar = CalendarIds.FirstCalendar(e);
            foreach (var o in RecurrenceEngine.Occurrences(e, zf, zt))
            {
                var (s, en) = allDay ? (o.Start, o.End) : (TimeZones.ZoneToLocal(o.Start, zone, local), TimeZones.ZoneToLocal(o.End, zone, local));
                if (!(s < t && en > f) && !(s == en && s >= f && s < t))
                {
                    continue;
                }

                var p = o.Patch;
                occ.Add(new CalendarOccurrence(
                    o.RecurrenceId is { } r ? $"{id}|{r}" : id,
                    id,
                    o.RecurrenceId,
                    p.Str("title") is { Length: > 0 } pt ? pt : title,
                    s,
                    en,
                    allDay,
                    calendar,
                    p is JsonObject po && po.ContainsKey("locations") ? LocationOf(p) : location,
                    CalendarOccurrenceKind.Event,
                    false));
            }
        }

        return [.. occ.OrderBy(o => o.Start).ThenBy(o => !o.AllDay)];
    }

    /// <summary>The tasks due in [from, to) as all-day occurrences "task:ID" on their due day (Rust occurrences_in, the task arm; shown when tasks and tasks-on-calendar are on). A task without a due date is not on the grid.</summary>
    public static IReadOnlyList<CalendarOccurrence> TasksIn(IReadOnlyList<JsonNode> events, DateOnly from, DateOnly to, TimeZoneInfo? local = null)
    {
        ArgumentNullException.ThrowIfNull(events);
        var f = from.ToDateTime(TimeOnly.MinValue);
        var t = to.ToDateTime(TimeOnly.MinValue);
        var occ = new List<CalendarOccurrence>();
        foreach (var e in events.Where(IsTask))
        {
            if (e.Str("due") is not { } dueText || RecurrenceEngine.ParseLocal(dueText) is not { } due)
            {
                continue;
            }

            if (!e.IsTrue("showWithoutTime"))
            {
                due = TimeZones.ZoneToLocal(due, e.Str("timeZone"), local);
            }

            var day = due.Date;
            if (day < f || day >= t)
            {
                continue;
            }

            var done = string.Equals(e.Str("progress"), "completed", StringComparison.Ordinal);
            var id = e.Str("id") ?? string.Empty;
            var title = e.Str("title") is { Length: > 0 } tt ? tt : "(untitled task)";
            occ.Add(new CalendarOccurrence($"task:{id}", id, null, $"{(done ? "✓" : "○")} {title}", day, day.AddDays(1), true, CalendarIds.FirstCalendar(e), string.Empty, CalendarOccurrenceKind.Task, done));
        }

        return [.. occ.OrderBy(o => o.Start)];
    }

    /// <summary>Whether the occurrence is on <paramref name="day"/> (an all-day occurrence ends the day before its end).</summary>
    public static bool Covers(CalendarOccurrence occurrence, DateOnly day)
    {
        ArgumentNullException.ThrowIfNull(occurrence);
        var last = occurrence.End > occurrence.Start ? occurrence.End.AddSeconds(-1).Date : occurrence.Start.Date;
        var d = day.ToDateTime(TimeOnly.MinValue);
        return occurrence.Start.Date <= d && last >= d;
    }

    /// <summary>Bulwark's isTaskObject: @type Task, or task-only fields on a CalDAV VTODO.</summary>
    public static bool IsTask(JsonNode ev)
    {
        if (string.Equals(ev.Str("@type"), "task", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.Equals(ev.Str("@type"), "Event", StringComparison.Ordinal) && (ev.IsString("progress") || ev.Child("due") is { } due && due.GetValueKind() != System.Text.Json.JsonValueKind.Null || ev.Child("percentComplete") is not null);
    }

    /// <summary>The event's place as one line: the first named location, else the first virtual location's address (Rust location_of).</summary>
    public static string LocationOf(JsonNode? ev)
    {
        if (ev.Obj("locations") is { } l)
        {
            foreach (var v in l.Select(kv => kv.Value))
            {
                if (v.Str("name") is { Length: > 0 } n)
                {
                    return n;
                }
            }
        }

        if (ev.Obj("virtualLocations") is { } vl)
        {
            foreach (var v in vl.Select(kv => kv.Value))
            {
                if (v.Str("uri") is { } u)
                {
                    return u;
                }
            }
        }

        return string.Empty;
    }

    /// <summary>"YYYY-MM-DD" of a day (the key the views and /changes tests use).</summary>
    internal static string DayKey(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
