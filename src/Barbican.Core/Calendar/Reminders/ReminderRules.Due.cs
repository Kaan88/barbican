// Mirrors: Rust reminders.rs:176 trigger_time, :186 instant, :220 due_between, :273 when_label, :320 missed; RFC 8984 4.5.2 (OffsetTrigger relativeTo start or end, AbsoluteTrigger; a task's start is its start, its end its due);
// Bulwark hooks/use-calendar-alerts.ts (stale threshold, missed list). G-005: alerts of an event far ahead are caught by the occurrence margin below (the longest alert plus two days), and the scheduler reads the event a year ahead. Owner cal-data.

using System.Globalization;
using System.Text.Json.Nodes;
using Barbican.Core.Calendar.Recurrence;

namespace Barbican.Core.Calendar.Reminders;

public static partial class ReminderRules
{
    /// <summary>The reminders firing in [fromMs, toMs): occurrences of series expanded, cancelled events and email-only alerts skipped, tasks included when <paramref name="tasks"/>. <paramref name="local"/> is the user's zone (default: the machine's).</summary>
    public static IReadOnlyList<DueReminder> DueBetween(IReadOnlyList<JsonNode> events, IReadOnlyList<JsonNode> calendars, long fromMs, long toMs, bool tasks, TimeZoneInfo? local = null)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(calendars);
        var output = new List<DueReminder>();
        foreach (var e in events)
        {
            if (string.Equals(e.Str("status"), "cancelled", StringComparison.Ordinal))
            {
                continue;
            }

            var calId = CalendarIds.FirstCalendar(e);
            var cal = calendars.FirstOrDefault(c => string.Equals(c.Str("id"), calId, StringComparison.Ordinal));
            var name = cal.Str("name") ?? string.Empty;
            if (CalendarOccurrences.IsTask(e))
            {
                if (tasks)
                {
                    output.AddRange(TaskDue(e, name, fromMs, toMs, local));
                }
            }
            else
            {
                output.AddRange(EventDue(e, cal, name, fromMs, toMs, local));
            }
        }

        return [.. output.OrderBy(d => d.FireMs)];
    }

    /// <summary>"In 15 minutes", "Now", "Task due" for the popup; two hours or more ahead it names the time of day (<paramref name="twelveHour"/> as the timeFormat setting, <paramref name="local"/> the user's zone).</summary>
    public static string WhenLabel(DueReminder due, long nowMs, bool twelveHour = false, TimeZoneInfo? local = null)
    {
        ArgumentNullException.ThrowIfNull(due);
        if (due.Task)
        {
            return "Task due";
        }

        if (due.StartMs is not { } s)
        {
            return "Now";
        }

        var mins = (long)Math.Round((s - nowMs) / 60000.0, MidpointRounding.AwayFromZero);
        if (mins <= 0)
        {
            return "Now";
        }

        if (mins == 1)
        {
            return "In 1 minute";
        }

        if (mins < 120)
        {
            return $"In {mins.ToString(CultureInfo.InvariantCulture)} minutes";
        }

        var at = TimeZones.UtcMsToZone(s, null, local);
        return "At " + at.ToString(twelveHour ? "h:mm tt" : "HH:mm", CultureInfo.InvariantCulture);
    }

    /// <summary>What the missed dialog lists: due after the last check (at most a week back) and before the stale threshold, not acknowledged; empty when there was no last check.</summary>
    public static IReadOnlyList<DueReminder> Missed(IReadOnlyList<DueReminder> due, IReadOnlySet<string> acknowledged, long? lastCheckMs, long nowMs)
    {
        ArgumentNullException.ThrowIfNull(due);
        ArgumentNullException.ThrowIfNull(acknowledged);
        if (lastCheckMs is not { } last)
        {
            return [];
        }

        var from = Math.Max(last, nowMs - MissedWindowMs);
        return [.. due.Where(d => d.FireMs >= from && d.FireMs <= nowMs - StaleMs && !acknowledged.Contains(d.Key))];
    }

    private static long? TriggerTime(JsonNode? trigger, long? startMs, long? endMs)
    {
        if (string.Equals(trigger.Str("@type"), "AbsoluteTrigger", StringComparison.Ordinal))
        {
            return trigger.Str("when") is { } w && DateTimeOffset.TryParse(w, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t.ToUnixTimeMilliseconds() : null;
        }

        if (trigger.Str("offset") is not { } offset || OffsetMs(offset) is not { } off)
        {
            return null;
        }

        return (string.Equals(trigger.Str("relativeTo"), "end", StringComparison.Ordinal) ? endMs : startMs) + off;
    }

    /// <summary>The instant of a wall clock of <paramref name="e"/> (its zone, or the user's for floating and all-day times).</summary>
    private static long Instant(JsonNode e, DateTime wall, TimeZoneInfo? local) => TimeZones.ZoneToUtcMs(wall, ZoneOf(e), local);

    private static string? ZoneOf(JsonNode e) => e.IsTrue("showWithoutTime") ? null : e.Str("timeZone") is { Length: > 0 } z ? z : null;

    private static bool Fires(JsonNode? alert) => string.Equals(alert.Str("action") ?? "display", "display", StringComparison.Ordinal) && !alert.IsString("acknowledged");

    private static IEnumerable<DueReminder> TaskDue(JsonNode e, string calendar, long fromMs, long toMs, TimeZoneInfo? local)
    {
        if (e.Str("progress") is "completed" or "cancelled" || e.Obj("alerts") is not { } alerts)
        {
            yield break;
        }

        var id = e.Str("id") ?? string.Empty;
        var title = e.Str("title") ?? string.Empty;
        long? due = e.Str("due") is { } d && RecurrenceEngine.ParseLocal(d) is { } dw ? Instant(e, dw, local) : null;
        long? start = e.Str("start") is { } s && RecurrenceEngine.ParseLocal(s) is { } sw ? Instant(e, sw, local) : null;
        foreach (var (aid, a) in alerts)
        {
            if (!Fires(a))
            {
                continue;
            }

            // RFC 8984: relativeTo start is a task's start, end its due; no start: its due
            var baseMs = !string.Equals(a.Child("trigger").Str("relativeTo"), "end", StringComparison.Ordinal) && start is not null ? start : due;
            if (TriggerTime(a.Child("trigger"), baseMs, due) is { } fire && fire >= fromMs && fire < toMs)
            {
                yield return new DueReminder(AlertKey(id, aid, fire), id, aid, fire, due, title, calendar, true);
            }
        }
    }

    private static IEnumerable<DueReminder> EventDue(JsonNode e, JsonNode? cal, string calendar, long fromMs, long toMs, TimeZoneInfo? local)
    {
        if (EffectiveAlerts(e, cal) is not JsonObject alerts || !alerts.Any(kv => string.Equals(kv.Value.Str("action") ?? "display", "display", StringComparison.Ordinal)))
        {
            yield break;
        }

        var id = e.Str("id") ?? string.Empty;
        var title = e.Str("title") ?? string.Empty;

        // occurrences whose start lies within a margin of the window (alerts can be weeks early)
        var longest = alerts.Select(kv => kv.Value.Child("trigger").Str("offset") is { } o ? OffsetMs(o) : null).OfType<long>().Select(Math.Abs).DefaultIfEmpty(0).Max();
        var margin = longest + (2 * DayMs);
        var zone = ZoneOf(e);
        var zf = TimeZones.UtcMsToZone(fromMs - margin, zone, local);
        var zt = TimeZones.UtcMsToZone(toMs + margin, zone, local);
        foreach (var o in RecurrenceEngine.Occurrences(e, zf, zt))
        {
            var (s, en) = (Instant(e, o.Start, local), Instant(e, o.End, local));
            var occTitle = o.Patch.Str("title") ?? title;
            foreach (var (aid, a) in alerts)
            {
                if (!Fires(a) || TriggerTime(a.Child("trigger"), s, en) is not { } fire || fire < fromMs || fire >= toMs)
                {
                    continue;
                }

                var eid = o.RecurrenceId is { } r ? $"{id}|{r}" : id;
                yield return new DueReminder(AlertKey(eid, aid, fire), eid, aid, fire, s, occTitle, calendar, false);
            }
        }
    }
}
