// Ported from Rust src/calendar.rs test :702 day_columns_follow_range (T-054); the all-day strip cap (G-003, Bulwark calendar-week-view.tsx:343-347) is real and green. M4 stream cal-views.

using Barbican.Presentation.Calendar.Grid;

namespace Barbican.Presentation.Tests.Calendar.Grid;

public sealed class CalendarGridRulesTests
{
    [Fact]
    [RustTest("calendar::day_columns_follow_range")]
    public void Day_columns_follow_range()
    {
        var d = new DateOnly(2026, 11, 16);
        Assert.Equal(3, CalendarGridRules.DayColumns(d, d.AddDays(3)).Count);
        Assert.Equal(d.AddDays(6), CalendarGridRules.DayColumns(d, d.AddDays(7))[^1]);
        Assert.Equal([d], CalendarGridRules.DayColumns(d, d.AddDays(1)));
    }

    [Fact]
    public void The_all_day_strip_is_capped_at_three_rows_until_expanded()
    {
        Assert.Equal(3, CalendarGridRules.AllDayVisibleRows(7, false));
        Assert.Equal(7, CalendarGridRules.AllDayVisibleRows(7, true));
        Assert.Equal(2, CalendarGridRules.AllDayVisibleRows(2, false));
        Assert.Equal(76, CalendarGridRules.AllDayHeight(3));
        Assert.Equal(28, CalendarGridRules.AllDayHeight(0));
        Assert.True(CalendarGridRules.AllDayExpandable(4));
        Assert.False(CalendarGridRules.AllDayExpandable(3));
    }
}
