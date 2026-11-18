// Mirrors: Rust recurrence.rs:13 weekday_of, :18 parse_local, :24 fmt_local, :27 duration_secs, :52 duration_string, :228 same_rules, :293 rule_starts, :377 event_duration, :386 occurrences, :427 occurrences_before;
// RFC 8984 4.3 (recurrence on the event's own wall clock, overrides, excludedRecurrenceRules), RFC 5545 3.3.6 (durations); Bulwark lib/recurrence-rule.ts, components/calendar/recurrence-editor.tsx.
// Wall-clock values are DateTime with Kind Unspecified (the engine uses NodaTime only for zones). Contract from the M4 plan section 4.1; owner cal-data replaces the bodies.

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Calendar.Recurrence;

/// <summary>JSCalendar recurrence expansion and the small date helpers the calendar shares. Pure; no clock, no zone database except through <see cref="TimeZones"/>.</summary>
public static class RecurrenceEngine
{
    /// <summary>A LocalDateTime "YYYY-MM-DDTHH:MM:SS" (a trailing Z or fraction is dropped; a bare date is midnight); null when it does not parse.</summary>
    public static DateTime? ParseLocal(string text) => throw new PortPendingException("RecurrenceEngine.ParseLocal");

    public static string FormatLocal(DateTime value) => throw new PortPendingException("RecurrenceEngine.FormatLocal");

    /// <summary>ISO 8601 duration (PnW, PnDTnHnMnS, optional sign) in seconds; null for "PT", "bogus".</summary>
    public static long? DurationSeconds(string text) => throw new PortPendingException("RecurrenceEngine.DurationSeconds");

    /// <summary>"PT1H30M", "P2D", "P1DT2H", "PT0S".</summary>
    public static string DurationString(long seconds) => throw new PortPendingException("RecurrenceEngine.DurationString");

    /// <summary>The event's duration; an all-day event lasts at least one day, a timed one defaults to one hour.</summary>
    public static TimeSpan EventDuration(JsonNode ev) => throw new PortPendingException("RecurrenceEngine.EventDuration");

    /// <summary>The start times of <paramref name="rule"/> from <paramref name="start"/> (the start counts as the first), before <paramref name="to"/>, at most <paramref name="max"/>.</summary>
    public static IReadOnlyList<DateTime> RuleStarts(DateTime start, RecurrenceRule rule, DateTime to, int max) => throw new PortPendingException("RecurrenceEngine.RuleStarts");

    /// <summary>The occurrences of <paramref name="ev"/> whose wall-clock span overlaps [from, to), with excluded overrides left out, moving overrides applied and override dates no rule gives added.</summary>
    public static IReadOnlyList<Occurrence> Occurrences(JsonNode ev, DateTime from, DateTime to) => throw new PortPendingException("RecurrenceEngine.Occurrences");

    /// <summary>How many occurrences of the rule (count ignored) come before <paramref name="split"/>: the COUNT left to a series split off there.</summary>
    public static long OccurrencesBefore(DateTime seriesStart, RecurrenceRule rule, DateTime split) => throw new PortPendingException("RecurrenceEngine.OccurrencesBefore");

    /// <summary>Two <c>recurrenceRules</c> arrays are the same when their parsed rules are (order of byDay, @type and default fields ignored).</summary>
    public static bool SameRules(JsonNode a, JsonNode b) => throw new PortPendingException("RecurrenceEngine.SameRules");
}
