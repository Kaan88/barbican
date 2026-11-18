// Mirrors: Rust recurrence.rs:119 Rule, :133 simple, :137 parse, :154 to_json, :172 is_simple, :178 summary, :215 nth_day; RFC 8984 4.3.2 RecurrenceRule; Bulwark components/calendar/recurrence-editor.tsx
// buildRecurrenceSummary, lib/recurrence-rule.ts. Contract from the M4 plan section 4.1; owner cal-data.

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Barbican.Core.Calendar.Recurrence;

/// <summary>A JSCalendar recurrence rule on the event's own wall clock. <c>Freq</c> is the JSCalendar word (daily, weekly, monthly, yearly; hourly, minutely, secondly are carried but not expanded).</summary>
public sealed record RecurrenceRule
{
    private static readonly string[] DayNames = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

    private static readonly string[] DayLong = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];

    private static readonly string[] MonthNames = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];

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
    public bool IsSimple =>
        Freq is "daily" or "weekly" or "monthly" or "yearly" && Interval == 1 && ByDay.Count == 0 && ByMonthDay.Count == 0 && ByMonth.Count == 0 && BySetPos.Count == 0 && Count is null && Until is null;

    /// <summary>The JSCalendar weekday codes, Monday first.</summary>
    internal static string[] DayCodes { get; } = ["mo", "tu", "we", "th", "fr", "sa", "su"];

    public static RecurrenceRule Simple(string freq) => new() { Freq = freq };

    /// <summary>Reads a JSCalendar RecurrenceRule object; never throws (a missing or odd field takes its default).</summary>
    public static RecurrenceRule Parse(JsonNode rule)
    {
        var byDay = new List<ByDayEntry>();
        if (rule.Arr("byDay") is { } days)
        {
            foreach (var d in days)
            {
                if (WeekdayOf(d.Str("day")) is { } w)
                {
                    var nth = d.Long("nthOfPeriod");
                    byDay.Add(new ByDayEntry(w, nth is null or 0 ? null : nth));
                }
            }
        }

        var months = new List<int>();
        if (rule.Arr("byMonth") is { } ms)
        {
            foreach (var m in ms)
            {
                // a list of strings ("3", "3L" for a leap month), or plain numbers
                var text = m is JsonValue { } v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>().TrimEnd('L') : m.AsLong()?.ToString(CultureInfo.InvariantCulture);
                if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n is >= 1 and <= 12)
                {
                    months.Add(n);
                }
            }
        }

        return new RecurrenceRule
        {
            Freq = (rule.Str("frequency") ?? "weekly").ToLowerInvariant(),
            Interval = Math.Max(1, rule.Long("interval") ?? 1),
            ByDay = byDay,
            ByMonthDay = Longs(rule.Arr("byMonthDay")),
            ByMonth = months,
            BySetPos = Longs(rule.Arr("bySetPosition")),
            Count = rule.Long("count") is > 0 and var c ? c : null,
            Until = rule.Str("until") is { } u ? RecurrenceEngine.ParseLocal(u) : null,
            FirstDayOfWeek = WeekdayOf(rule.Str("firstDayOfWeek")) ?? DayOfWeek.Monday,
        };
    }

    /// <summary>The JSCalendar object (@type RecurrenceRule, frequency, interval when not 1, byDay, byMonthDay, byMonth as strings, bySetPosition, count, until, firstDayOfWeek when not Monday).</summary>
    public JsonObject ToJson()
    {
        var v = new JsonObject { ["@type"] = "RecurrenceRule", ["frequency"] = Freq };
        if (Interval != 1)
        {
            v["interval"] = Num(Interval);
        }

        if (ByDay.Count > 0)
        {
            v["byDay"] = new JsonArray([.. ByDay.Select(d =>
            {
                var o = new JsonObject { ["@type"] = "NDay", ["day"] = DayCodes[MondayIndex(d.Day)] };
                if (d.Nth is { } n)
                {
                    o["nthOfPeriod"] = Num(n);
                }

                return (JsonNode)o;
            })]);
        }

        if (ByMonthDay.Count > 0)
        {
            v["byMonthDay"] = new JsonArray([.. ByMonthDay.Select(x => Num(x))]);
        }

        if (ByMonth.Count > 0)
        {
            v["byMonth"] = new JsonArray([.. ByMonth.Select(m => (JsonNode)m.ToString(CultureInfo.InvariantCulture))]);
        }

        if (BySetPos.Count > 0)
        {
            v["bySetPosition"] = new JsonArray([.. BySetPos.Select(x => Num(x))]);
        }

        if (Count is { } c)
        {
            v["count"] = Num(c);
        }

        if (Until is { } u)
        {
            v["until"] = RecurrenceEngine.FormatLocal(u);
        }

        if (FirstDayOfWeek != DayOfWeek.Monday)
        {
            v["firstDayOfWeek"] = DayCodes[MondayIndex(FirstDayOfWeek)];
        }

        return v;
    }

    /// <summary>Bulwark's English summary ("Every 2 weeks on Mon, Wed · 12 occurrences", "Monthly on the third Thursday", "Yearly in March on day 15 · until Mar 15, 2030").</summary>
    public string Summary()
    {
        var n = Interval;
        string Unit(string one, string many) => n > 1 ? $"Every {n.ToString(CultureInfo.InvariantCulture)} {many}" : one;
        var s = Freq switch
        {
            "daily" => Unit("Daily", "days"),
            "weekly" => Unit("Weekly", "weeks"),
            "monthly" => Unit("Monthly", "months"),
            "yearly" => Unit("Yearly", "years"),
            "hourly" => Unit("Hourly", "hours"),
            "minutely" => Unit("Every minute", "minutes"),
            _ => Freq,
        };
        if (string.Equals(Freq, "weekly", StringComparison.Ordinal) && ByDay.Count > 0)
        {
            var d = ByDay.Select(x => MondayIndex(x.Day)).Distinct().Order().Select(i => DayNames[i]);
            s += " on " + string.Join(", ", d);
        }

        if (Freq is "monthly" or "yearly")
        {
            if (string.Equals(Freq, "yearly", StringComparison.Ordinal) && ByMonth.Count > 0)
            {
                s += " in " + MonthNames[Math.Min(ByMonth[0] - 1, 11)];
            }

            if (NthDay() is { } nth)
            {
                s += $" on the {NthLabel(nth.Nth ?? 0)} {DayLong[MondayIndex(nth.Day)]}";
            }
            else if (ByMonthDay.Count > 0)
            {
                s += " on day " + ByMonthDay[0].ToString(CultureInfo.InvariantCulture);
            }
        }

        if (Count is { } c)
        {
            s += $" · {c.ToString(CultureInfo.InvariantCulture)} occurrence{(c == 1 ? string.Empty : "s")}";
        }
        else if (Until is { } u)
        {
            s += " · until " + u.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
        }

        return s;
    }

    /// <summary>The single "nth weekday of the month" of the rule (byDay with nthOfPeriod, or one byDay with bySetPosition), if that is what it is.</summary>
    public ByDayEntry? NthDay()
    {
        if (ByDay.Count != 1)
        {
            return null;
        }

        var d = ByDay[0];
        if (d.Nth is not null)
        {
            return d;
        }

        return BySetPos.Count == 1 ? d with { Nth = BySetPos[0] } : null;
    }

    public bool Equals(RecurrenceRule? other) =>
        other is not null && string.Equals(Freq, other.Freq, StringComparison.Ordinal) && Interval == other.Interval && Count == other.Count && Until == other.Until && FirstDayOfWeek == other.FirstDayOfWeek
        && ByDay.SequenceEqual(other.ByDay) && ByMonthDay.SequenceEqual(other.ByMonthDay) && ByMonth.SequenceEqual(other.ByMonth) && BySetPos.SequenceEqual(other.BySetPos);

    public override int GetHashCode() => HashCode.Combine(Freq, Interval, Count, Until, FirstDayOfWeek, ByDay.Count, ByMonthDay.Count, ByMonth.Count);

    /// <summary>0 for Monday to 6 for Sunday (Rust's num_days_from_monday).</summary>
    internal static int MondayIndex(DayOfWeek d) => ((int)d + 6) % 7;

    internal static DayOfWeek? WeekdayOf(string? code)
    {
        var i = Array.FindIndex(DayCodes, d => string.Equals(d, code, StringComparison.OrdinalIgnoreCase));
        return i < 0 ? null : (DayOfWeek)((i + 1) % 7);
    }

    private static string NthLabel(long n) => n switch
    {
        -1 => "last",
        1 => "first",
        2 => "second",
        3 => "third",
        4 => "fourth",
        5 => "fifth",
        _ => n.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>A JSON number node that holds an int when it fits, so callers can read it back with an int read (a long-typed node refuses the conversion).</summary>
    private static JsonValue Num(long n) => (n is >= int.MinValue and <= int.MaxValue ? JsonValue.Create((int)n) : JsonValue.Create(n))!;

    private static List<long> Longs(JsonArray? a) => a is null ? [] : [.. a.Select(x => x.AsLong()).OfType<long>()];
}
