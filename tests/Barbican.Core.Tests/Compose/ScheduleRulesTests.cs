#pragma warning disable MA0002, MA0006 // asserts compare strings ordinally by nature (the ported Rust asserts)
using Barbican.Core.Compose.Schedule;

namespace Barbican.Core.Tests.Compose;

/// <summary>Rust src/schedule.rs tests, ported (M3 stream send-pipeline). Times are local to a fixed UTC+1 zone so the tests do not depend on the machine zone or DST (D-242).</summary>
public sealed class ScheduleRulesTests
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("test+1", TimeSpan.FromHours(1), "test+1", "test+1");

    private static DateTimeOffset Local(int y, int mo, int d, int h, int mi) => new(y, mo, d, h, mi, 0, TimeSpan.FromHours(1));

    [Fact]
    [RustTest("schedule::preset_times")]
    public void Preset_times()
    {
        // Monday 2026-11-16 at 07:00: both of today's times, tomorrow's, next Monday
        var p = ScheduleRules.Presets(Local(2026, 11, 16, 7, 0), Zone);
        Assert.Equal(["This morning", "This afternoon", "Tomorrow morning", "Tomorrow afternoon", "Monday morning"], p.Select(x => x.Label));
        Assert.Equal(Local(2026, 11, 23, 8, 0), p[4].At);

        // 07:58: this morning is less than 5 minutes away
        Assert.Equal("This afternoon", ScheduleRules.Presets(Local(2026, 11, 16, 7, 58), Zone)[0].Label);

        // Sunday evening: Monday is tomorrow, so no Monday preset
        var s = ScheduleRules.Presets(Local(2026, 11, 22, 20, 0), Zone);
        Assert.Equal(["Tomorrow morning", "Tomorrow afternoon"], s.Select(x => x.Label));

        // Saturday: Monday in two days
        Assert.Equal(Local(2026, 11, 23, 8, 0), ScheduleRules.Presets(Local(2026, 11, 21, 20, 0), Zone)[^1].At);
    }

    [Fact]
    [RustTest("schedule::parsing_and_checks")]
    public void Parsing_and_checks()
    {
        Assert.Equal<DateTimeOffset?>(Local(2026, 11, 17, 8, 0), ScheduleRules.ParseLocal("2026-11-17", "08:00", Zone));
        Assert.Equal<DateTimeOffset?>(Local(2026, 11, 17, 20, 30), ScheduleRules.ParseLocal("17.11.2026", "8:30 pm", Zone));
        Assert.Equal<DateTimeOffset?>(Local(2026, 11, 17, 0, 0), ScheduleRules.ParseLocal("17/11/2026", "12 am", Zone));
        Assert.Equal<DateTimeOffset?>(Local(2026, 11, 17, 20, 15), ScheduleRules.ParseLocal("2026-11-17", "20.15", Zone));
        Assert.Null(ScheduleRules.ParseLocal("2026-02-30", "08:00", Zone));
        Assert.Null(ScheduleRules.ParseLocal("2026-11-17", "25:00", Zone));

        var now = Local(2026, 11, 16, 12, 0);
        const long week = 7 * 86400;
        Assert.True(ScheduleRules.Validate(Local(2026, 11, 17, 8, 0), now, week, true).IsOk);
        Assert.Equal("Choose a date and time.", ScheduleRules.Validate(null, now, week, false).Error?.Message);
        Assert.Equal("Enter a valid date and time.", ScheduleRules.Validate(null, now, week, true).Error?.Message);
        Assert.Equal("Choose a future date and time.", ScheduleRules.Validate(Local(2026, 11, 16, 11, 0), now, week, true).Error?.Message);
        Assert.Equal("This time is later than the server allows.", ScheduleRules.Validate(Local(2026, 11, 30, 8, 0), now, week, true).Error?.Message);
        Assert.Equal("Scheduled send is not supported for this account.", ScheduleRules.Validate(Local(2026, 11, 17, 8, 0), now, 0, true).Error?.Message);
    }

    [Fact]
    [RustTest("schedule::labels")]
    public void Labels()
    {
        var now = Local(2026, 11, 16, 12, 0);

        // a second early rounds up to the minute picked
        var early = Local(2026, 11, 17, 8, 0) - TimeSpan.FromSeconds(1);
        Assert.Equal("Tue 17 Nov, 08:00", ScheduleRules.Label(early, false, now, Zone));
        Assert.Equal("1:00 PM", ScheduleRules.Label(Local(2026, 11, 16, 13, 0), true, now, Zone));
    }
}
