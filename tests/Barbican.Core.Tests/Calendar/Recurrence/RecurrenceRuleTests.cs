// Ported from Rust src/recurrence.rs tests :515 summaries, :524 rule_json_round_trip_and_sameness (T-238, T-239). M4 stream cal-data.

using Barbican.Core.Calendar.Recurrence;
using static Barbican.Core.Tests.Calendar.CalendarTestData;

namespace Barbican.Core.Tests.Calendar.Recurrence;

public sealed class RecurrenceRuleTests
{
    [Fact]
    [Red("cal-data")]
    [RustTest("recurrence::summaries")]
    public void Summaries()
    {
        Assert.Equal("Every 2 weeks on Mon, Wed · 12 occurrences", RecurrenceRule.Parse(J("""{"frequency": "weekly", "interval": 2, "byDay": [{"day": "we"}, {"day": "mo"}], "count": 12}""")).Summary());
        Assert.Equal("Monthly on the third Thursday", RecurrenceRule.Parse(J("""{"frequency": "monthly", "byDay": [{"day": "th", "nthOfPeriod": 3}]}""")).Summary());
        Assert.Equal("Yearly in March on day 15 · until Mar 15, 2030", RecurrenceRule.Parse(J("""{"frequency": "yearly", "byMonth": ["3"], "byMonthDay": [15], "until": "2030-03-15T23:59:59"}""")).Summary());
        Assert.True(RecurrenceRule.Parse(J("""{"frequency": "daily"}""")).IsSimple);
        Assert.False(RecurrenceRule.Parse(J("""{"frequency": "daily", "count": 3}""")).IsSimple);
    }

    [Fact]
    [Red("cal-data")]
    [RustTest("recurrence::rule_json_round_trip_and_sameness")]
    public void Rule_json_round_trip_and_sameness()
    {
        var r = RecurrenceRule.Parse(J("""{"frequency": "weekly", "byDay": [{"day": "mo"}, {"day": "fr"}], "until": "2027-01-01T23:59:59"}"""));
        Assert.Equal(r, RecurrenceRule.Parse(r.ToJson()));
        Assert.True(RecurrenceEngine.SameRules(
            J("""[{"frequency": "weekly", "interval": 1, "byDay": [{"day": "fr"}, {"day": "mo"}]}]"""),
            J("""[{"@type": "RecurrenceRule", "frequency": "weekly", "byDay": [{"day": "mo"}, {"day": "fr"}], "firstDayOfWeek": "mo"}]""")));
        Assert.False(RecurrenceEngine.SameRules(J("""[{"frequency": "weekly"}]"""), J("""[{"frequency": "daily"}]""")));
    }
}
