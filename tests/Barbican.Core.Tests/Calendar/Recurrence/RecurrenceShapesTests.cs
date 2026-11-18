// M4 cal-data acceptance tests beyond the ported Rust ones: more rule shapes, DST on the event's own wall clock, all-day multi-day series (M4 plan 6.1 "Acceptance tests").

using System.Globalization;
using Barbican.Core.Calendar;
using Barbican.Core.Calendar.Recurrence;
using static Barbican.Core.Tests.Calendar.CalendarTestData;

namespace Barbican.Core.Tests.Calendar.Recurrence;

public sealed class RecurrenceShapesTests
{
    private static readonly DateTime Far = new(2040, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

    private static string[] Days(string start, string rule) =>
        [.. RecurrenceEngine.RuleStarts(Dt(start), RecurrenceRule.Parse(J(rule)), Far, 1000).Select(d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))];

    [Fact]
    public void Weekly_across_a_year_end() =>
        Assert.Equal(["2026-12-28", "2027-01-04", "2027-01-11", "2027-01-18"], Days("2026-12-28T09:00:00", """{"frequency": "weekly", "count": 4}"""));

    [Fact]
    public void The_fifth_weekday_of_a_month_skips_months_without_one() =>
        Assert.Equal(["2026-10-30", "2027-01-29", "2027-04-30", "2027-07-30"], Days("2026-10-30T09:00:00", """{"frequency": "monthly", "byDay": [{"day": "fr", "nthOfPeriod": 5}], "count": 4}"""));

    [Fact]
    public void By_month_day_minus_one_is_the_last_day_of_each_month() =>
        Assert.Equal(["2027-01-31", "2027-02-28", "2027-03-31", "2027-04-30"], Days("2027-01-31T09:00:00", """{"frequency": "monthly", "byMonthDay": [-1], "count": 4}"""));

    [Fact]
    public void Monthly_with_interval_two() =>
        Assert.Equal(["2026-11-15", "2027-01-15", "2027-03-15"], Days("2026-11-15T09:00:00", """{"frequency": "monthly", "interval": 2, "count": 3}"""));

    [Fact]
    public void A_leap_day_yearly_series_only_falls_on_leap_years() =>
        Assert.Equal(["2028-02-29", "2032-02-29", "2036-02-29"], Days("2028-02-29T09:00:00", """{"frequency": "yearly", "count": 3}"""));

    [Fact]
    public void Until_before_the_start_leaves_only_the_start() =>
        Assert.Equal(["2026-11-05"], Days("2026-11-05T09:00:00", """{"frequency": "daily", "until": "2026-11-01T09:00:00"}"""));

    [Fact]
    public void Daily_by_day_gives_the_weekdays() =>
        Assert.Equal(["2026-11-06", "2026-11-09", "2026-11-10", "2026-11-11", "2026-11-12"], Days("2026-11-06T09:00:00", """{"frequency": "daily", "byDay": [{"day": "mo"}, {"day": "tu"}, {"day": "we"}, {"day": "th"}, {"day": "fr"}], "count": 5}"""));

    [Fact]
    public void Yearly_in_a_month_on_the_first_monday() =>
        Assert.Equal(["2026-03-02", "2027-03-01", "2028-03-06"], Days("2026-03-02T09:00:00", """{"frequency": "yearly", "byMonth": ["3"], "byDay": [{"day": "mo", "nthOfPeriod": 1}], "count": 3}"""));

    [Fact]
    public void Monthly_on_two_days_of_the_month() =>
        Assert.Equal(["2026-11-01", "2026-11-15", "2026-12-01", "2026-12-15"], Days("2026-11-01T09:00:00", """{"frequency": "monthly", "byMonthDay": [1, 15], "count": 4}"""));

    [Fact]
    public void Hourly_with_interval_six()
    {
        var v = RecurrenceEngine.RuleStarts(Dt("2026-11-01T00:00:00"), RecurrenceRule.Parse(J("""{"frequency": "hourly", "interval": 6, "count": 4}""")), Far, 100);
        Assert.Equal(["00", "06", "12", "18"], v.Select(d => d.ToString("HH", CultureInfo.InvariantCulture)).ToArray());
    }

    [Fact]
    public void A_runaway_rule_ends()
    {
        // a rule that never matches (Feb 30) must end through the guards, with the start only
        var v = RecurrenceEngine.RuleStarts(Dt("2026-11-01T09:00:00"), RecurrenceRule.Parse(J("""{"frequency": "yearly", "byMonth": ["2"], "byMonthDay": [30]}""")), Far, 100);
        Assert.Single(v);
    }

    [Fact]
    public void Excluded_recurrence_rules_remove_occurrences()
    {
        var e = J("""{"start": "2026-11-02T09:00:00", "duration": "PT1H", "recurrenceRules": [{"frequency": "daily", "count": 6}], "excludedRecurrenceRules": [{"frequency": "weekly", "byDay": [{"day": "sa"}, {"day": "su"}], "count": 5}]}""");
        var o = RecurrenceEngine.Occurrences(e, Dt("2026-11-01T00:00:00"), Dt("2026-12-01T00:00:00"));
        Assert.DoesNotContain(o, x => x.Start.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);
        // Rust counts the start as the first start of the excluded rule too, so the Monday start goes with it (kept as Rust: excludedRecurrenceRules are rare)
        Assert.Equal(4, o.Count);
    }

    [Fact]
    public void A_daily_0230_event_in_Berlin_keeps_its_wall_clock_across_both_DST_changes()
    {
        var e = J("""{"id": "e", "start": "2026-03-27T02:30:00", "timeZone": "Europe/Berlin", "duration": "PT30M", "recurrenceRules": [{"frequency": "daily", "count": 5}]}""");

        // the occurrences are on the event's own clock: 02:30 every day, also on the day it does not exist
        var own = RecurrenceEngine.Occurrences(e, Dt("2026-03-01T00:00:00"), Dt("2026-04-30T00:00:00"));
        Assert.All(own, o => Assert.Equal(new TimeSpan(2, 30, 0), o.Start.TimeOfDay));

        // on the user's clock (UTC here): CET is UTC+1, CEST UTC+2; the skipped 02:30 of 29 March is read as 03:30 CEST (Rust), the same instant as 01:30 UTC
        var utc = TimeZoneInfo.Utc;
        var shown = CalendarOccurrences.In([e], D("2026-03-26"), D("2026-04-05"), utc).Select(o => o.Start.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture)).ToArray();
        Assert.Equal(["03-27 01:30", "03-28 01:30", "03-29 01:30", "03-30 00:30", "03-31 00:30"], shown);

        // and back in autumn: 02:30 of 25 October happens twice; the first (CEST) is taken, every time
        var autumn = J("""{"id": "a", "start": "2026-10-24T02:30:00", "timeZone": "Europe/Berlin", "duration": "PT30M", "recurrenceRules": [{"frequency": "daily", "count": 3}]}""");
        var seen = CalendarOccurrences.In([autumn], D("2026-10-23"), D("2026-10-30"), utc).Select(o => o.Start.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture)).ToArray();
        Assert.Equal(["10-24 00:30", "10-25 00:30", "10-26 01:30"], seen);
    }

    [Fact]
    public void The_missing_and_the_doubled_hour_convert_the_same_way_every_time()
    {
        var utc = TimeZoneInfo.Utc;
        Assert.Equal(UtcMs("2026-03-29T01:30:00"), TimeZones.ZoneToUtcMs(Dt("2026-03-29T02:30:00"), "Europe/Berlin", utc));
        Assert.Equal(UtcMs("2026-10-25T00:30:00"), TimeZones.ZoneToUtcMs(Dt("2026-10-25T02:30:00"), "Europe/Berlin", utc));
        Assert.Equal(Dt("2026-03-29T01:30:00"), TimeZones.ZoneToLocal(Dt("2026-03-29T02:30:00"), "Europe/Berlin", utc));
        Assert.Equal(UtcMs("2026-07-01T10:00:00"), TimeZones.ZoneToUtcMs(Dt("2026-07-01T12:00:00"), "Europe/Berlin", utc));

        // an unknown zone is floating: the wall time is already the user's
        Assert.Equal(Dt("2026-07-01T12:00:00"), TimeZones.ZoneToLocal(Dt("2026-07-01T12:00:00"), "Mars/Olympus", utc));
        Assert.Equal(UtcMs("2026-07-01T12:00:00"), TimeZones.ZoneToUtcMs(Dt("2026-07-01T12:00:00"), "Mars/Olympus", utc));
    }

    [Fact]
    public void An_all_day_multi_day_series_covers_each_of_its_days()
    {
        var e = J("""{"id": "a", "title": "Trip", "start": "2026-11-02T00:00:00", "showWithoutTime": true, "duration": "P2D", "recurrenceRules": [{"frequency": "weekly", "count": 3}], "calendarIds": {"c": true}}""");
        var o = CalendarOccurrences.In([e], D("2026-11-01"), D("2026-12-01"), TimeZoneInfo.Utc);
        Assert.Equal(3, o.Count);
        Assert.All(o, x => Assert.True(x.AllDay));
        Assert.Equal("a|2026-11-09T00:00:00", o[1].Id);
        Assert.True(CalendarOccurrences.Covers(o[1], D("2026-11-09")));
        Assert.True(CalendarOccurrences.Covers(o[1], D("2026-11-10")));
        Assert.False(CalendarOccurrences.Covers(o[1], D("2026-11-11")));
        Assert.Equal("c", o[0].Calendar);
    }

    [Fact]
    public void Tasks_are_on_their_due_day_and_not_events()
    {
        var t = J("""{"id": "t1", "@type": "Task", "title": "Pay", "due": "2026-11-06T09:00:00", "timeZone": "UTC", "calendarIds": {"c": true}}""");
        var d = CalendarOccurrences.TasksIn([t], D("2026-11-01"), D("2026-12-01"), TimeZoneInfo.Utc);
        Assert.Equal("task:t1", Assert.Single(d).Id);
        Assert.Equal(CalendarOccurrenceKind.Task, d[0].Kind);
        Assert.True(CalendarOccurrences.IsTask(t));
        Assert.False(CalendarOccurrences.IsTask(J("""{"@type": "Event", "due": "2026-11-06T09:00:00"}""")));
        Assert.True(CalendarOccurrences.IsTask(J("""{"progress": "needs-action"}""")));
    }
}
