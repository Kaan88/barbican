// New tests, vectors from Bulwark lib/list-date-groups.ts (listDateGroup): Today, Yesterday, the weekday for the rest of the last seven days,
// Last week, Last month, Older. Spec I-119. Owner mailcore.

using Barbican.Core.Mail.Grouping;

namespace Barbican.Core.Tests.Mail.Grouping;

public sealed class DateGroupsTests
{
    // Tuesday 17 November 2026, 12:00 UTC
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    private static ListDateGroup Group(string date, string? zoneId = null) =>
        DateGroups.GroupOf(DateTimeOffset.Parse(date, System.Globalization.CultureInfo.InvariantCulture), Now, zoneId is null ? TimeZoneInfo.Utc : TimeZoneInfo.FindSystemTimeZoneById(zoneId));

    [Fact]
    public void Today_yesterday_and_the_future()
    {
        Assert.Equal(ListDateGroup.Today, Group("2026-11-17T00:00:01Z"));
        Assert.Equal(ListDateGroup.Today, Group("2026-11-18T09:00:00Z"));
        Assert.Equal(ListDateGroup.Yesterday, Group("2026-11-16T23:59:59Z"));
    }

    [Fact]
    public void The_rest_of_the_last_seven_days_are_named_by_weekday()
    {
        Assert.Equal(ListDateGroup.OnWeekday(DayOfWeek.Sunday), Group("2026-11-15T10:00:00Z"));
        Assert.Equal(ListDateGroup.OnWeekday(DayOfWeek.Wednesday), Group("2026-11-11T10:00:00Z"));
        Assert.Equal("Sunday", Group("2026-11-15T10:00:00Z").Label);
        Assert.NotEqual(ListDateGroup.OnWeekday(DayOfWeek.Tuesday), Group("2026-11-10T10:00:00Z"));
    }

    [Fact]
    public void Last_week_last_month_and_older()
    {
        Assert.Equal(ListDateGroup.LastWeek, Group("2026-11-10T10:00:00Z"));
        Assert.Equal(ListDateGroup.LastWeek, Group("2026-11-04T10:00:00Z"));
        Assert.Equal(ListDateGroup.LastMonth, Group("2026-11-03T10:00:00Z"));
        Assert.Equal(ListDateGroup.LastMonth, Group("2026-10-17T10:00:00Z"));
        Assert.Equal(ListDateGroup.Older, Group("2026-10-16T10:00:00Z"));
        Assert.Equal("Last week", ListDateGroup.LastWeek.Label);
        Assert.Equal("Older", ListDateGroup.Older.Label);
    }

    [Fact]
    public void Last_month_reaches_back_to_the_last_day_of_a_shorter_month()
    {
        // 31 March looks back to 28 February (2027 is no leap year)
        var now = new DateTimeOffset(2027, 3, 31, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(ListDateGroup.LastMonth, DateGroups.GroupOf(new DateTimeOffset(2027, 2, 28, 1, 0, 0, TimeSpan.Zero), now, TimeZoneInfo.Utc));
        Assert.Equal(ListDateGroup.Older, DateGroups.GroupOf(new DateTimeOffset(2027, 2, 27, 23, 0, 0, TimeSpan.Zero), now, TimeZoneInfo.Utc));
    }

    [Fact]
    public void Days_are_counted_in_the_given_zone()
    {
        // 23:30 UTC the day before is already "today" at UTC+2
        var zone = TimeZoneInfo.CreateCustomTimeZone("plus2", TimeSpan.FromHours(2), "plus2", "plus2");
        Assert.Equal(ListDateGroup.Today, DateGroups.GroupOf(new DateTimeOffset(2026, 11, 16, 23, 30, 0, TimeSpan.Zero), Now, zone));
        Assert.Equal(ListDateGroup.Yesterday, DateGroups.GroupOf(new DateTimeOffset(2026, 11, 16, 23, 30, 0, TimeSpan.Zero), Now, TimeZoneInfo.Utc));
    }
}
