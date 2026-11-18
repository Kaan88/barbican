// Mirrors: Rust recurrence.rs:119 Rule, :133 simple, :137 parse, :154 to_json, :172 is_simple, :178 summary, :215 nth_day; RFC 8984 4.3.2 RecurrenceRule; Bulwark components/calendar/recurrence-editor.tsx
// buildRecurrenceSummary, lib/recurrence-rule.ts. Contract from the M4 plan section 4.1; owner cal-data replaces the throwing members. The record and its equality are real.

using System.Text.Json.Nodes;
using Barbican.Core.Common;

#pragma warning disable CA1822 // the stubs below do not read the instance yet; the real bodies do

namespace Barbican.Core.Calendar.Recurrence;

/// <summary>A JSCalendar recurrence rule on the event's own wall clock. <c>Freq</c> is the JSCalendar word (daily, weekly, monthly, yearly; hourly, minutely, secondly are carried but not expanded).</summary>
public sealed record RecurrenceRule
{
    public string Freq { get; init; } = "daily";

    public long Interval { get; init; } = 1;

    public IReadOnlyList<ByDayEntry> ByDay { get; init; } = [];

    public IReadOnlyList<long> ByMonthDay { get; init; } = [];

    /// <summary>Months 1 to 12 (JSCalendar writes them as strings, "3").</summary>
    public IReadOnlyList<int> ByMonth { get; init; } = [];

    public IReadOnlyList<long> BySetPos { get; init; } = [];

    public long? Count { get; init; }

    /// <summary>Inclusive, on the event's wall clock (Kind Unspecified).</summary>
    public DateTime? Until { get; init; }

    public DayOfWeek FirstDayOfWeek { get; init; } = DayOfWeek.Monday;

    /// <summary>No count, no until, interval 1 and no by-parts: what the "Repeat" menu calls a plain daily, weekly, monthly or yearly repeat.</summary>
    public bool IsSimple => throw new PortPendingException("RecurrenceRule.IsSimple");

    public static RecurrenceRule Simple(string freq) => new() { Freq = freq };

    /// <summary>Reads a JSCalendar RecurrenceRule object; never throws (a missing or odd field takes its default).</summary>
    public static RecurrenceRule Parse(JsonNode rule) => throw new PortPendingException("RecurrenceRule.Parse");

    /// <summary>The JSCalendar object (@type RecurrenceRule, frequency, interval when not 1, byDay, byMonthDay, byMonth as strings, bySetPosition, count, until, firstDayOfWeek when not Monday).</summary>
    public JsonObject ToJson() => throw new PortPendingException("RecurrenceRule.ToJson");

    /// <summary>Bulwark's English summary ("Every 2 weeks on Mon, Wed · 12 occurrences", "Monthly on the third Thursday", "Yearly in March on day 15 · until Mar 15, 2030").</summary>
    public string Summary() => throw new PortPendingException("RecurrenceRule.Summary");

    /// <summary>The single "nth weekday of the month" of the rule (byDay with nthOfPeriod, or one byDay with bySetPosition), if that is what it is.</summary>
    public ByDayEntry? NthDay() => throw new PortPendingException("RecurrenceRule.NthDay");

    public bool Equals(RecurrenceRule? other) =>
        other is not null && string.Equals(Freq, other.Freq, StringComparison.Ordinal) && Interval == other.Interval && Count == other.Count && Until == other.Until && FirstDayOfWeek == other.FirstDayOfWeek
        && ByDay.SequenceEqual(other.ByDay) && ByMonthDay.SequenceEqual(other.ByMonthDay) && ByMonth.SequenceEqual(other.ByMonth) && BySetPos.SequenceEqual(other.BySetPos);

    public override int GetHashCode() => HashCode.Combine(Freq, Interval, Count, Until, FirstDayOfWeek, ByDay.Count, ByMonthDay.Count, ByMonth.Count);
}
