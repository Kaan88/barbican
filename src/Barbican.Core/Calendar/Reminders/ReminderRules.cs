// Mirrors: Rust reminders.rs:19 STALE_MS, :21 MISSED_WINDOW_MS, :24 FETCH_AHEAD_MS, :27 fetch_ahead, :49 offset_ms, :53 offset_to_unit, :72 describe, :80 row_offset, :88 row_label, :95 alerts_label, :105 calendar_defaults,
// :113 effective_alerts, :120 rows_of, :135 with_own_ids, :157 build_alerts_patch, :202 alert_key, :220 due_between, :273 when_label, :320 missed; RFC 8984 4.5.2 (Alert, OffsetTrigger, useDefaultAlerts);
// Bulwark hooks/use-calendar-alerts.ts, lib/calendar-alerts.ts, components/calendar/alert-list-editor.tsx. G-005: an event's own alerts are read from the event (the fetch window is 8 days, 15 with long default alerts).
// Contract from the M4 plan section 4.1; owner cal-data replaces the bodies.

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Calendar.Reminders;

/// <summary>The pure reminder rules: alert rows, effective alerts, the patch the editor sends, when alerts fire and which ones were missed.</summary>
public static class ReminderRules
{
    /// <summary>A reminder older than this when first seen is stale and not shown (10 minutes).</summary>
    public const long StaleMs = 10 * 60 * 1000;

    /// <summary>How far back the missed-reminders dialog looks (7 days).</summary>
    public const long MissedWindowMs = 7L * 24 * 3600 * 1000;

    /// <summary>The read-ahead window: 8 days, or the longest calendar default alert plus a day (15 days for a 14 day default).</summary>
    public static long FetchAhead(IReadOnlyList<JsonNode> calendars) => throw new PortPendingException("ReminderRules.FetchAhead");

    /// <summary>"-PT15M" is -900000.</summary>
    public static long? OffsetMs(string offset) => throw new PortPendingException("ReminderRules.OffsetMs");

    /// <summary>Bulwark offsetToAlertUnit: an offset at or before the start as (amount, unit); offsets after the start and compound ones ("-P1DT2H") cannot be rows (null).</summary>
    public static (long Value, int Unit)? OffsetToUnit(string offset) => throw new PortPendingException("ReminderRules.OffsetToUnit");

    /// <summary>The row as a JSCalendar offset ("-PT2H"); null for 0 with a unit other than "at time of event".</summary>
    public static string? RowOffset(ReminderRow row) => throw new PortPendingException("ReminderRules.RowOffset");

    /// <summary>"1 day before by email", "10 minutes before", "At time of event".</summary>
    public static string RowLabel(ReminderRow row) => throw new PortPendingException("ReminderRules.RowLabel");

    /// <summary>The row of an Alert object; null for an absolute trigger or one that cannot be a row.</summary>
    public static ReminderRow? Describe(JsonNode alert) => throw new PortPendingException("ReminderRules.Describe");

    /// <summary>The label of an alerts map ("10 minutes before"; "None" when empty).</summary>
    public static string AlertsLabel(JsonNode? alerts) => throw new PortPendingException("ReminderRules.AlertsLabel");

    /// <summary>The calendar's default alerts for a timed or an all-day event.</summary>
    public static JsonNode? CalendarDefaults(JsonNode? calendar, bool allDay) => throw new PortPendingException("ReminderRules.CalendarDefaults");

    /// <summary>The alerts that fire: the event's own copy when it has one, else the calendar defaults when <c>useDefaultAlerts</c> is on; null when none.</summary>
    public static JsonNode? EffectiveAlerts(JsonNode ev, JsonNode? calendar) => throw new PortPendingException("ReminderRules.EffectiveAlerts");

    /// <summary>The editor rows of an alerts map, and the alerts that cannot be rows (kept untouched).</summary>
    public static (IReadOnlyList<AlertEditRow> Rows, JsonObject Kept) RowsOf(JsonNode? alerts) => throw new PortPendingException("ReminderRules.RowsOf");

    /// <summary>Gives new alerts ids (alert1, alert2, ...) that avoid <paramref name="taken"/>.</summary>
    public static JsonObject WithOwnIds(IReadOnlyList<JsonNode> alerts, IReadOnlyList<string> taken) => throw new PortPendingException("ReminderRules.WithOwnIds");

    /// <summary>The alerts part of an event save: unchanged rows keep their alert and id, nothing is sent when nothing changed, all rows removed clears (null), a new event says <c>useDefaultAlerts</c> false and sends no empty map.</summary>
    public static JsonObject BuildAlertsPatch(JsonNode? ev, IReadOnlyList<AlertEditRow> rows, JsonObject preserved, IReadOnlyList<string> reserved) => throw new PortPendingException("ReminderRules.BuildAlertsPatch");

    /// <summary><c>event:alert:fireMs</c>.</summary>
    public static string AlertKey(string ev, string alert, long fireMs) => throw new PortPendingException("ReminderRules.AlertKey");

    /// <summary>The reminders firing in [fromMs, toMs): occurrences of series expanded, cancelled events and email-only alerts skipped, tasks included when <paramref name="tasks"/>.</summary>
    public static IReadOnlyList<DueReminder> DueBetween(IReadOnlyList<JsonNode> events, IReadOnlyList<JsonNode> calendars, long fromMs, long toMs, bool tasks) => throw new PortPendingException("ReminderRules.DueBetween");

    /// <summary>"In 15 minutes", "Now", "15 minutes ago" for the popup.</summary>
    public static string WhenLabel(DueReminder due, long nowMs) => throw new PortPendingException("ReminderRules.WhenLabel");

    /// <summary>What the missed dialog lists: due after the last check (at most a week back) and before the stale threshold, not acknowledged; empty when there was no last check.</summary>
    public static IReadOnlyList<DueReminder> Missed(IReadOnlyList<DueReminder> due, IReadOnlySet<string> acknowledged, long? lastCheckMs, long nowMs) => throw new PortPendingException("ReminderRules.Missed");
}
