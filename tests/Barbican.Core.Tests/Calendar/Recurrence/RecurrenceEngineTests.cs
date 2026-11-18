// Ported from Rust src/recurrence.rs tests (:439-:539): durations, weekly/monthly/yearly expansion, until, overrides, count left (T-231..T-237, T-240). M4 stream cal-data.

using System.Globalization;
using Barbican.Core.Calendar.Recurrence;
using static Barbican.Core.Tests.Calendar.CalendarTestData;

namespace Barbican.Core.Tests.Calendar.Recurrence;

public sealed class RecurrenceEngineTests
{
    private static string[] Fmt(IEnumerable<DateTime> v, string format) => [.. v.Select(d => d.ToString(format, CultureInfo.InvariantCulture))];

    [Fact]
    [RustTest("recurrence::durations_round_trip")]
    public void Durations_round_trip()
    {
        Assert.Equal(5400, RecurrenceEngine.DurationSeconds("PT1H30M"));
        Assert.Equal(86400, RecurrenceEngine.DurationSeconds("P1D"));
        Assert.Equal(-900, RecurrenceEngine.DurationSeconds("-PT15M"));
        Assert.Equal(604800, RecurrenceEngine.DurationSeconds("P1W"));
        Assert.Equal(93600, RecurrenceEngine.DurationSeconds("P1DT2H"));
        Assert.Null(RecurrenceEngine.DurationSeconds("PT"));
        Assert.Null(RecurrenceEngine.DurationSeconds("bogus"));
        Assert.Equal("PT1H30M", RecurrenceEngine.DurationString(5400));
        Assert.Equal("P2D", RecurrenceEngine.DurationString(86400 * 2));
        Assert.Equal("PT0S", RecurrenceEngine.DurationString(0));
        Assert.Equal("P1DT2H", RecurrenceEngine.DurationString(93600));
    }

    [Fact]
    [RustTest("recurrence::weekly_by_day_interval_count")]
    public void Weekly_by_day_interval_count()
    {
        var r = RecurrenceRule.Parse(J("""{"frequency": "weekly", "interval": 2, "byDay": [{"day": "mo"}, {"day": "we"}], "count": 5}"""));
        var v = RecurrenceEngine.RuleStarts(Dt("2026-11-02T09:00:00"), r, Dt("2027-12-01T00:00:00"), 100);
        Assert.Equal(["11-02", "11-04", "11-16", "11-18", "11-30"], Fmt(v, "MM-dd"));
    }

    [Fact]
    [RustTest("recurrence::until_is_inclusive_and_start_counts")]
    public void Until_is_inclusive_and_start_counts()
    {
        var r = RecurrenceRule.Parse(J("""{"frequency": "daily", "until": "2026-11-05T09:00:00"}"""));
        Assert.Equal(5, RecurrenceEngine.RuleStarts(Dt("2026-11-01T09:00:00"), r, Dt("2027-01-01T00:00:00"), 100).Count);
        r = RecurrenceRule.Parse(J("""{"frequency": "daily", "count": 1}"""));
        Assert.Single(RecurrenceEngine.RuleStarts(Dt("2026-11-01T09:00:00"), r, Dt("2027-01-01T00:00:00"), 100));
    }

    [Fact]
    [RustTest("recurrence::monthly_nth_and_last_weekday")]
    public void Monthly_nth_and_last_weekday()
    {
        var r = RecurrenceRule.Parse(J("""{"frequency": "monthly", "byDay": [{"day": "th", "nthOfPeriod": 3}], "count": 3}"""));
        var v = RecurrenceEngine.RuleStarts(Dt("2026-11-19T10:00:00"), r, Dt("2028-01-01T00:00:00"), 100);
        Assert.Equal(["2026-11-19", "2026-12-17", "2027-01-21"], Fmt(v, "yyyy-MM-dd"));
        r = RecurrenceRule.Parse(J("""{"frequency": "monthly", "byDay": [{"day": "fr", "nthOfPeriod": -1}], "count": 2}"""));
        v = RecurrenceEngine.RuleStarts(Dt("2026-11-27T10:00:00"), r, Dt("2028-01-01T00:00:00"), 100);
        Assert.Equal("2026-12-25", v[1].ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        // bySetPosition spelling of the same pattern
        r = RecurrenceRule.Parse(J("""{"frequency": "monthly", "byDay": [{"day": "fr"}], "bySetPosition": [-1], "count": 2}"""));
        Assert.Equal("2026-12-25", RecurrenceEngine.RuleStarts(Dt("2026-11-27T10:00:00"), r, Dt("2028-01-01T00:00:00"), 100)[1].ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    [Fact]
    [RustTest("recurrence::monthly_day_31_skips_short_months")]
    public void Monthly_day_31_skips_short_months()
    {
        var r = RecurrenceRule.Parse(J("""{"frequency": "monthly", "count": 3}"""));
        var v = RecurrenceEngine.RuleStarts(Dt("2027-01-31T08:00:00"), r, Dt("2028-01-01T00:00:00"), 100);
        Assert.Equal(["01-31", "03-31", "05-31"], Fmt(v, "MM-dd"));
    }

    [Fact]
    [RustTest("recurrence::yearly_in_month_on_day")]
    public void Yearly_in_month_on_day()
    {
        var r = RecurrenceRule.Parse(J("""{"frequency": "yearly", "byMonth": ["3"], "byMonthDay": [15]}"""));
        var v = RecurrenceEngine.RuleStarts(Dt("2026-03-15T00:00:00"), r, Dt("2029-01-01T00:00:00"), 100);
        Assert.Equal(3, v.Count);
        Assert.Equal("2028-03-15", v[2].ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    [Fact]
    [RustTest("recurrence::overrides_exclusions_and_moves")]
    public void Overrides_exclusions_and_moves()
    {
        var e = J("""
            {"start": "2026-11-02T09:00:00", "duration": "PT1H", "recurrenceRules": [{"frequency": "daily", "count": 4}],
             "recurrenceOverrides": {"2026-11-03T09:00:00": {"excluded": true}, "2026-11-04T09:00:00": {"start": "2026-11-04T15:00:00", "title": "Moved"}}}
            """);
        var o = RecurrenceEngine.Occurrences(e, Dt("2026-11-01T00:00:00"), Dt("2026-12-01T00:00:00"));
        Assert.Equal(3, o.Count);
        Assert.Equal(Dt("2026-11-04T15:00:00"), o[1].Start);
        Assert.Equal("2026-11-04T09:00:00", o[1].RecurrenceId);
        Assert.Equal("Moved", (string?)o[1].Patch!["title"]);

        // a single event in range, and out of it
        var s = J("""{"start": "2026-11-02T09:00:00", "duration": "PT1H"}""");
        Assert.Single(RecurrenceEngine.Occurrences(s, Dt("2026-11-02T00:00:00"), Dt("2026-11-03T00:00:00")));
        Assert.Empty(RecurrenceEngine.Occurrences(s, Dt("2026-11-03T00:00:00"), Dt("2026-11-04T00:00:00")));
    }

    [Fact]
    [RustTest("recurrence::count_left_after_split")]
    public void Count_left_after_split()
    {
        var r = RecurrenceRule.Parse(J("""{"frequency": "daily", "count": 10}"""));
        Assert.Equal(3, RecurrenceEngine.OccurrencesBefore(Dt("2026-11-01T09:00:00"), r, Dt("2026-11-04T09:00:00")));
    }
}
