// The differential test of the recurrence engine (M4 plan 6.1 step 1, D-M4-03): 300 seeded random rules (daily, weekly, monthly, yearly; interval 1 to 3; byDay with and without nth; count or until) expanded by RecurrenceEngine.RuleStarts
// and by Ical.Net (the oracle: test-side only, never the engine). The rule's DTSTART is chosen to match its own pattern, because RFC 5545 only counts a DTSTART that fits the rule while Rust (and so the port) always counts the start.
// Documented differences, not skipped silently: see D-M4-A01 in spec/decisions.md and the tests at the end.

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Barbican.Core.Calendar.Recurrence;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using static Barbican.Core.Tests.Calendar.CalendarTestData;

namespace Barbican.Core.Tests.Calendar.Recurrence;

public sealed class RecurrenceOracleTests
{
    private static readonly string[] Codes = ["MO", "TU", "WE", "TH", "FR", "SA", "SU"];

    private static readonly DateTime Horizon = new(2031, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

    private static List<DateTime> Oracle(DateTime start, string rrule)
    {
        var e = new CalendarEvent { DtStart = new CalDateTime(start), DtEnd = new CalDateTime(start.AddHours(1)) };
        e.RecurrenceRules.Add(new RecurrencePattern(rrule));
        return [.. e.GetOccurrences(new CalDateTime(start)).TakeWhile(o => o.Period.StartTime.Value < Horizon).Select(o => DateTime.SpecifyKind(o.Period.StartTime.Value, DateTimeKind.Unspecified))];
    }

    private static string Rrule(string freq, long interval, string? byDay, string? tail) =>
        $"FREQ={freq.ToUpperInvariant()};INTERVAL={interval.ToString(CultureInfo.InvariantCulture)}" + (byDay is null ? string.Empty : ";BYDAY=" + byDay) + (tail is null ? string.Empty : ";" + tail);

    /// <summary>The rule as the JSCalendar object the engine reads.</summary>
    private static RecurrenceRule FromRrule(string rrule)
    {
        var o = new JsonObject();
        foreach (var part in rrule.Split(';'))
        {
            var eq = part.IndexOf('=', StringComparison.Ordinal);
            var (k, v) = (part[..eq], part[(eq + 1)..]);
            switch (k)
            {
                case "FREQ":
                    o["frequency"] = v.ToLowerInvariant();
                    break;
                case "INTERVAL":
                    o["interval"] = long.Parse(v, CultureInfo.InvariantCulture);
                    break;
                case "COUNT":
                    o["count"] = long.Parse(v, CultureInfo.InvariantCulture);
                    break;
                case "UNTIL":
                    o["until"] = DateTime.ParseExact(v, "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture).ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
                    break;
                case "BYDAY":
                    o["byDay"] = new JsonArray([.. v.Split(',').Select(ByDayNode)]);
                    break;
                case "BYSETPOS":
                    o["bySetPosition"] = new JsonArray([.. v.Split(',').Select(x => (JsonNode)long.Parse(x, CultureInfo.InvariantCulture))]);
                    break;
                default:
                    throw new InvalidOperationException(k);
            }
        }

        return RecurrenceRule.Parse(o);
    }

    private static JsonNode ByDayNode(string d)
    {
        var nd = new JsonObject { ["day"] = d[^2..].ToLowerInvariant() };
        if (d.Length > 2)
        {
            nd["nthOfPeriod"] = long.Parse(d[..^2], CultureInfo.InvariantCulture);
        }

        return nd;
    }

    private static DateTime NthWeekday(int year, int month, DayOfWeek day, int nth)
    {
        var all = Enumerable.Range(1, DateTime.DaysInMonth(year, month)).Select(d => new DateTime(year, month, d, 0, 0, 0, DateTimeKind.Unspecified)).Where(d => d.DayOfWeek == day).ToList();
        return nth > 0 ? all[nth - 1] : all[^1];
    }

    private static DayOfWeek Dow(int i) => (DayOfWeek)((i + 1) % 7);

    private static string Brief(IEnumerable<DateTime> v) => string.Join(' ', v.Take(6).Select(d => d.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture)));

    [Fact]
    public void Three_hundred_random_rules_match_Ical_Net()
    {
        var rng = new Random(4231);
        var differences = new StringBuilder();
        var total = 0;
        for (var i = 0; i < 300; i++)
        {
            var freq = new[] { "daily", "weekly", "monthly", "yearly" }[i % 4];
            var interval = rng.Next(1, 4);
            var day0 = new DateTime(2026, 1, 1, rng.Next(0, 23), rng.Next(0, 4) * 15, 0, DateTimeKind.Unspecified).AddDays(rng.Next(0, 700));
            string? byDay = null;
            var start = day0;
            switch (freq)
            {
                case "weekly" when rng.Next(2) == 0:
                {
                    var picks = Enumerable.Range(0, 7).OrderBy(_ => rng.Next()).Take(rng.Next(1, 4)).Order().ToList();
                    byDay = string.Join(',', picks.Select(p => Codes[p]));

                    // the start is the first day from there that the pattern names (RFC 5545: DTSTART fits the rule)
                    start = Enumerable.Range(0, 7).Select(d => day0.AddDays(d)).First(d => picks.Contains(((int)d.DayOfWeek + 6) % 7));
                    break;
                }

                case "monthly" when rng.Next(2) == 0:
                {
                    var wd = rng.Next(0, 7);
                    var nth = new[] { 1, 2, 3, 4, -1 }[rng.Next(5)];
                    byDay = nth.ToString(CultureInfo.InvariantCulture) + Codes[wd];
                    start = NthWeekday(day0.Year, day0.Month, Dow(wd), nth) + day0.TimeOfDay;
                    break;
                }

                case "monthly":
                case "yearly":
                    start = new DateTime(day0.Year, day0.Month, Math.Min(day0.Day, 28), day0.Hour, day0.Minute, 0, DateTimeKind.Unspecified);
                    break;
                default:
                    break;
            }

            var tail = rng.Next(2) == 0
                ? "COUNT=" + rng.Next(3, 21).ToString(CultureInfo.InvariantCulture)
                : "UNTIL=" + start.AddDays(rng.Next(20, 900)).ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture);
            var rrule = Rrule(freq, interval, byDay, tail);
            var mine = RecurrenceEngine.RuleStarts(start, FromRrule(rrule), Horizon, 10_000);
            var theirs = Oracle(start, rrule);
            total += theirs.Count;
            if (!mine.SequenceEqual(theirs))
            {
                _ = differences.AppendLine(CultureInfo.InvariantCulture, $"{rrule} from {start:s}: engine {Brief(mine)} ({mine.Count}), Ical.Net {Brief(theirs)} ({theirs.Count})");
            }
        }

        Assert.True(differences.Length == 0, differences.ToString());
        Assert.True(total > 2000, "the oracle must give real occurrences, got " + total.ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Day_31_monthly_skips_short_months_in_both_engines()
    {
        // both engines skip the months without a 31st (RFC 5545 3.3.10): a check, not a difference
        var start = Dt("2027-01-31T08:00:00");
        const string rrule = "FREQ=MONTHLY;INTERVAL=1;COUNT=6";
        Assert.Equal(Oracle(start, rrule), RecurrenceEngine.RuleStarts(start, FromRrule(rrule), Horizon, 100));
    }

    [Fact]
    public void The_last_weekday_of_each_month_by_set_position_matches()
    {
        // bySetPosition over several byDay: the one shape the plan named as a possible difference; here both engines agree
        var start = Dt("2026-01-30T09:00:00");
        const string rrule = "FREQ=MONTHLY;INTERVAL=1;BYDAY=MO,TU,WE,TH,FR;BYSETPOS=-1;COUNT=8";
        Assert.Equal(Oracle(start, rrule), RecurrenceEngine.RuleStarts(start, FromRrule(rrule), Horizon, 100));
    }

    [Fact]
    public void A_start_that_does_not_fit_its_rule_still_counts_as_the_first_occurrence()
    {
        // the documented difference (D-M4-A01): RFC 5545 leaves a DTSTART that does not fit the pattern out, Rust and so this port count it (COUNT includes it)
        var start = Dt("2026-11-03T09:00:00");
        var rule = RecurrenceRule.Parse(J("""{"frequency": "weekly", "byDay": [{"day": "we"}], "count": 3}"""));
        var mine = RecurrenceEngine.RuleStarts(start, rule, Horizon, 100);
        Assert.Equal(["2026-11-03", "2026-11-04", "2026-11-11"], mine.Select(d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).ToArray());
        Assert.NotEqual(mine, Oracle(start, "FREQ=WEEKLY;BYDAY=WE;COUNT=3"));
    }
}
