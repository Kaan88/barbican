// M4 stream cal-ui acceptance: the custom recurrence editor produces the rules of the Rust tests (weekly every 2 weeks on Friday; monthly nth weekday; yearly; ends) and keeps one weekday on (cal_editor.rs rr_rule, rr_toggle_day).

using Barbican.Core.Calendar.Recurrence;
using Barbican.Presentation.Calendar.Editors;

namespace Barbican.Presentation.Tests.Calendar.Editors;

public sealed class RecurrenceEditorViewModelTests
{
    private static readonly DateTime Friday = new(2026, 11, 20, 10, 0, 0, DateTimeKind.Unspecified);

    private static RecurrenceEditorViewModel Editor() => new(new FakeRules());

    [Fact]
    public void A_fresh_editor_is_weekly_on_the_start_weekday()
    {
        var e = Editor();
        e.Load(null, Friday);
        Assert.Equal("weekly", e.Rule.Freq);
        Assert.Equal([new ByDayEntry(DayOfWeek.Friday)], e.Rule.ByDay);
        Assert.Equal(1, e.Rule.Interval);
    }

    [Fact]
    public void Every_two_weeks_on_friday_and_monday_in_week_order()
    {
        var e = Editor();
        e.Load(null, Friday);
        e.Interval = "2";
        e.ToggleDay(DayOfWeek.Monday);
        Assert.Equal([new ByDayEntry(DayOfWeek.Monday), new ByDayEntry(DayOfWeek.Friday)], e.Rule.ByDay);
        Assert.Equal(2, e.Rule.Interval);
    }

    [Fact]
    public void At_least_one_weekday_stays_on()
    {
        var e = Editor();
        e.Load(null, Friday);
        e.ToggleDay(DayOfWeek.Friday);
        Assert.True(e.IsDayOn(DayOfWeek.Friday));
        Assert.Single(e.Weekdays);
    }

    [Fact]
    public void Monthly_on_the_last_friday_and_on_day_fifteen()
    {
        var e = Editor();
        e.Load(null, Friday);
        e.Freq = 2;
        e.Mode = 1;
        e.Nth = 4;
        e.NthDay = DayOfWeek.Friday;
        Assert.Equal([new ByDayEntry(DayOfWeek.Friday, -1)], e.Rule.ByDay);
        Assert.Empty(e.Rule.ByMonthDay);
        e.Mode = 0;
        e.MonthDay = "15";
        Assert.Equal([15L], e.Rule.ByMonthDay);
        Assert.Empty(e.Rule.ByDay);
    }

    [Fact]
    public void Yearly_in_a_month_and_the_end_choices()
    {
        var e = Editor();
        e.Load(null, Friday);
        e.Freq = 3;
        e.Month = 2;
        e.MonthDay = "15";
        Assert.Equal([3], e.Rule.ByMonth);
        e.Ends = 2;
        e.Count = "12";
        Assert.Equal(12, e.Rule.Count);
        Assert.Null(e.Rule.Until);
        e.Ends = 1;
        e.Until = "2030-03-15";
        Assert.Equal(new DateTime(2030, 3, 15, 23, 59, 59, DateTimeKind.Unspecified), e.Rule.Until);
        Assert.Null(e.Rule.Count);
        e.Ends = 0;
        Assert.Null(e.Rule.Until);
    }

    [Fact]
    public void An_existing_rule_loads_back_into_the_editor()
    {
        var rule = new RecurrenceRule { Freq = "monthly", Interval = 3, ByDay = [new ByDayEntry(DayOfWeek.Thursday, 3)], Count = 6 };
        var e = Editor();
        e.Load(rule, Friday);
        Assert.Equal(2, e.Freq);
        Assert.Equal("3", e.Interval);
        Assert.Equal(1, e.Mode);
        Assert.Equal(2, e.Nth);
        Assert.Equal(DayOfWeek.Thursday, e.NthDay);
        Assert.Equal(2, e.Ends);
        Assert.Equal("6", e.Count);
        Assert.Equal(rule.ByDay, e.Rule.ByDay);
    }

    [Fact]
    public void The_summary_is_the_summary_of_the_rule()
    {
        var e = Editor();
        e.Load(null, Friday);
        Assert.Equal(new FakeRules().Summary(e.Rule), e.Summary);
    }
}
