// Mirrors: Rust reminders.rs:19 STALE_MS, :21 MISSED_WINDOW_MS, :24 FETCH_AHEAD_MS, :27 fetch_ahead, :49 offset_ms, :53 offset_to_unit, :72 describe, :80 row_offset, :88 row_label, :95 alerts_label, :105 calendar_defaults,
// :113 effective_alerts, :120 rows_of, :135 with_own_ids, :157 build_alerts_patch, :202 alert_key, :220 due_between, :273 when_label, :320 missed; RFC 8984 4.5.2 (Alert, OffsetTrigger, useDefaultAlerts);
// Bulwark hooks/use-calendar-alerts.ts, lib/calendar-alerts.ts, components/calendar/alert-list-editor.tsx (formatAlertRowLabel, AlertSummary: "No reminders", "N other"). G-005: an event's own alerts are read from the event
// (the fetch window is 8 days, 15 with long default alerts). Contract from the M4 plan section 4.1; owner cal-data.

using System.Globalization;
using System.Text.Json.Nodes;
using Barbican.Core.Calendar.Recurrence;

namespace Barbican.Core.Calendar.Reminders;

/// <summary>The pure reminder rules: alert rows, effective alerts, the patch the editor sends, when alerts fire and which ones were missed.</summary>
public static partial class ReminderRules
{
    /// <summary>A reminder older than this when first seen is stale and not shown (10 minutes).</summary>
    public const long StaleMs = 10 * 60 * 1000;

    /// <summary>How far back the missed-reminders dialog looks (7 days).</summary>
    public const long MissedWindowMs = 7L * 24 * 3600 * 1000;

    private const long DayMs = 86_400_000;

    private static readonly string[] DefaultKeys = ["defaultAlertsWithTime", "defaultAlertsWithoutTime"];

    private static readonly string[] ZeroOffsets = ["PT0S", "PT0M", "PT0H", "P0D", "P0W", "PT0D"];

    /// <summary>The read-ahead window: 8 days, or the longest calendar default alert plus a day (15 days for a 14 day default).</summary>
    public static long FetchAhead(IReadOnlyList<JsonNode> calendars)
    {
        ArgumentNullException.ThrowIfNull(calendars);
        var longest = calendars
            .SelectMany(c => DefaultKeys.Select(k => c.Obj(k)).OfType<JsonObject>())
            .SelectMany(m => m.Select(kv => kv.Value))
            .Select(a => a.Child("trigger").Str("offset") is { } o ? OffsetMs(o) : null)
            .OfType<long>()
            .Select(Math.Abs)
            .DefaultIfEmpty(0)
            .Max();
        return Math.Max(8 * DayMs, longest + DayMs);
    }

    /// <summary>"-PT15M" is -900000.</summary>
    public static long? OffsetMs(string offset) => offset is null ? null : RecurrenceEngine.DurationSeconds(offset) * 1000;

    /// <summary>Bulwark offsetToAlertUnit: an offset at or before the start as (amount, unit); offsets after the start and compound ones ("-P1DT2H") cannot be rows (null).</summary>
    public static (long Value, int Unit)? OffsetToUnit(string offset)
    {
        ArgumentNullException.ThrowIfNull(offset);
        var o = offset.Trim();
        if (ZeroOffsets.Contains(o.TrimStart('-'), StringComparer.Ordinal))
        {
            return (0, 0);
        }

        if (!o.StartsWith('-'))
        {
            return null;
        }

        var rest = o[1..];
        static long? Num(string s) => long.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n) ? n : null;
        if (rest.StartsWith('P') && rest.EndsWith('W') && Num(rest[1..^1]) is { } w)
        {
            return (w, 4);
        }

        if (rest.StartsWith('P') && rest.EndsWith('D') && Num(rest[1..^1]) is { } d)
        {
            return (d, 3);
        }

        if (rest.StartsWith("PT", StringComparison.Ordinal) && rest.EndsWith('H') && Num(rest[2..^1]) is { } h)
        {
            return (h, 2);
        }

        if (rest.StartsWith("PT", StringComparison.Ordinal) && rest.EndsWith('M') && Num(rest[2..^1]) is { } m)
        {
            return m > 0 && m % 1440 == 0 ? (m / 1440, 3) : m > 0 && m % 60 == 0 ? (m / 60, 2) : (m, 1);
        }

        return null;
    }

    /// <summary>The row as a JSCalendar offset ("-PT2H"); null for 0 with a unit other than "at time of event".</summary>
    public static string? RowOffset(ReminderRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.Unit == 0)
        {
            return "PT0S";
        }

        var v = row.Value;
        if (v <= 0)
        {
            return null;
        }

        var n = v.ToString(CultureInfo.InvariantCulture);
        return row.Unit switch
        {
            1 => $"-PT{n}M",
            2 => $"-PT{n}H",
            3 => $"-P{n}D",
            _ => $"-P{n}W",
        };
    }

    /// <summary>"1 day before by email", "10 minutes before", "At time of event".</summary>
    public static string RowLabel(ReminderRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        static string Plural(long n, string w) => $"{n.ToString(CultureInfo.InvariantCulture)} {w}{(n == 1 ? string.Empty : "s")} before";
        var l = row.Unit switch
        {
            0 => "At time of event",
            1 => Plural(row.Value, "minute"),
            2 => Plural(row.Value, "hour"),
            3 => Plural(row.Value, "day"),
            _ => Plural(row.Value, "week"),
        };
        return row.Action == 1 ? l + " by email" : l;
    }

    /// <summary>The row of an Alert object; null for an absolute trigger or one that cannot be a row.</summary>
    public static ReminderRow? Describe(JsonNode alert)
    {
        var trigger = alert.Child("trigger");
        if (!string.Equals(trigger.Str("@type") ?? "OffsetTrigger", "OffsetTrigger", StringComparison.Ordinal) || string.Equals(trigger.Str("relativeTo"), "end", StringComparison.Ordinal))
        {
            return null;
        }

        var action = (alert.Str("action") ?? "display") switch
        {
            "display" => 0,
            "email" => 1,
            _ => -1,
        };
        if (action < 0 || trigger.Str("offset") is not { } offset || OffsetToUnit(offset) is not { } unit)
        {
            return null;
        }

        return new ReminderRow(unit.Value, unit.Unit, action);
    }

    /// <summary>The label of an alerts map ("10 minutes before"; "No reminders" when empty, Bulwark alerts.none; alerts that are not rows count as "N other").</summary>
    public static string AlertsLabel(JsonNode? alerts)
    {
        if (alerts is not JsonObject { Count: > 0 } m)
        {
            return "No reminders";
        }

        var labels = new List<string>();
        var other = 0;
        foreach (var a in m.Select(kv => kv.Value))
        {
            if (a is not null && Describe(a) is { } r)
            {
                labels.Add(RowLabel(r));
            }
            else
            {
                other++;
            }
        }

        if (other > 0)
        {
            labels.Add($"{other.ToString(CultureInfo.InvariantCulture)} other");
        }

        return string.Join(", ", labels);
    }

    /// <summary>The calendar's default alerts for a timed or an all-day event.</summary>
    public static JsonNode? CalendarDefaults(JsonNode? calendar, bool allDay) =>
        calendar.Obj(allDay ? "defaultAlertsWithoutTime" : "defaultAlertsWithTime") is { Count: > 0 } a ? a.DeepClone() : null;

    /// <summary>The alerts that fire: the event's own copy when it has one, else the calendar defaults when <c>useDefaultAlerts</c> is on; null when none.</summary>
    public static JsonNode? EffectiveAlerts(JsonNode ev, JsonNode? calendar)
    {
        var own = ev.Obj("alerts") is { Count: > 0 } a ? a.DeepClone() : null;
        if (!ev.IsTrue("useDefaultAlerts"))
        {
            return own;
        }

        return own ?? CalendarDefaults(calendar, ev.IsTrue("showWithoutTime"));
    }

    /// <summary>The editor rows of an alerts map, and the alerts that cannot be rows (kept untouched).</summary>
    public static (IReadOnlyList<AlertEditRow> Rows, JsonObject Kept) RowsOf(JsonNode? alerts)
    {
        var rows = new List<AlertEditRow>();
        var kept = new JsonObject();
        if (alerts is JsonObject m)
        {
            foreach (var (id, a) in m)
            {
                if (a is not null && Describe(a) is { } r)
                {
                    rows.Add(new AlertEditRow(r, new AlertSource(id, r)));
                }
                else
                {
                    kept[id] = a?.DeepClone();
                }
            }
        }

        return (rows, kept);
    }

    /// <summary>Gives new alerts ids (alert1, alert2, ...) that avoid <paramref name="taken"/>.</summary>
    public static JsonObject WithOwnIds(IReadOnlyList<JsonNode> alerts, IReadOnlyList<string> taken)
    {
        ArgumentNullException.ThrowIfNull(alerts);
        ArgumentNullException.ThrowIfNull(taken);
        var output = new JsonObject();
        var n = 0;
        foreach (var a in alerts)
        {
            string id;
            do
            {
                n++;
                id = "alert" + n.ToString(CultureInfo.InvariantCulture);
            }
            while (taken.Contains(id, StringComparer.Ordinal));
            output[id] = a.DeepClone();
        }

        return output;
    }

    /// <summary>The alerts part of an event save: unchanged rows keep their alert and id, nothing is sent when nothing changed, all rows removed clears (null), a new event says <c>useDefaultAlerts</c> false and sends no empty map.</summary>
    public static JsonObject BuildAlertsPatch(JsonNode? ev, IReadOnlyList<AlertEditRow> rows, JsonObject preserved, IReadOnlyList<string> reserved)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(preserved);
        ArgumentNullException.ThrowIfNull(reserved);
        var original = ev.Obj("alerts") ?? [];
        var (kept, added) = KeepOrAdd(rows, preserved, original);

        var usesDefaults = ev is not null && ev.IsTrue("useDefaultAlerts");
        var leavesDefaults = ev is null || usesDefaults;
        var patch = new JsonObject();
        if (!leavesDefaults && added.Count == 0 && original.All(kv => kept.ContainsKey(kv.Key)))
        {
            return patch;
        }

        var taken = original.Select(kv => kv.Key).Concat(reserved).ToList();
        JsonObject alerts;
        if (usesDefaults)
        {
            alerts = WithOwnIds([.. kept.Select(kv => kv.Value).OfType<JsonNode>(), .. added], taken);
        }
        else
        {
            alerts = kept;
            foreach (var (k, v) in WithOwnIds(added, taken))
            {
                alerts[k] = v?.DeepClone();
            }
        }

        if (leavesDefaults)
        {
            patch["useDefaultAlerts"] = false;
        }

        if (alerts.Count > 0)
        {
            patch["alerts"] = alerts;
        }
        else if (ev is not null)
        {
            patch["alerts"] = null;
        }

        return patch;
    }

    /// <summary>Which rows keep the alert they were read from (under its id) and which become new alerts.</summary>
    private static (JsonObject Kept, List<JsonNode> Added) KeepOrAdd(IReadOnlyList<AlertEditRow> rows, JsonObject preserved, JsonObject original)
    {
        var kept = (JsonObject)preserved.DeepClone();
        var added = new List<JsonNode>();
        foreach (var r in rows)
        {
            if (RowOffset(r.Row) is not { } offset)
            {
                continue;
            }

            if (r.Source is { } src && src.Row == r.Row && original.ContainsKey(src.Id))
            {
                kept[src.Id] = original[src.Id]!.DeepClone();
                continue;
            }

            added.Add(NewAlert(offset, r.Row.Action));
        }

        return (kept, added);
    }

    /// <summary><c>event:alert:fireMs</c>.</summary>
    public static string AlertKey(string ev, string alert, long fireMs) => $"{ev}:{alert}:{fireMs.ToString(CultureInfo.InvariantCulture)}";

    private static JsonObject NewAlert(string offset, int action) => new()
    {
        ["@type"] = "Alert",
        ["trigger"] = new JsonObject { ["@type"] = "OffsetTrigger", ["offset"] = offset, ["relativeTo"] = "start" },
        ["action"] = action == 1 ? "email" : "display",
    };
}
