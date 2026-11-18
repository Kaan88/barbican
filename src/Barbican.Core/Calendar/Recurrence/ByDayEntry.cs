// Mirrors: Rust recurrence.rs:119 Rule.by_day; RFC 8984 4.3.2 NDay (day, nthOfPeriod). Contract from the M4 plan section 4.1; owner cal-data.

namespace Barbican.Core.Calendar.Recurrence;

/// <summary>One BYDAY entry: a weekday and, for monthly and yearly rules, which one of the period (1 to 4, -1 for the last).</summary>
public sealed record ByDayEntry(DayOfWeek Day, long? Nth = null);
