// Green: CalendarSettings.Read defaults equal Rust's (calendar.rs:160 cal_prefs); spec/settings.csv. M4 (real contract code).

using Barbican.Core.Calendar;
using Barbican.Core.Storage;

namespace Barbican.Core.Tests.Calendar;

public sealed class CalendarSettingsTests
{
    [Fact]
    public void Defaults_follow_the_rust_app()
    {
        var s = CalendarSettings.Read(PrefsSnapshot.FromJson([]));
        Assert.Equal(1, s.FirstDayOfWeek);
        Assert.True(s.LimitHours);
        Assert.Equal(20, s.DayEndHour);
        Assert.Equal("bar", s.MonthTimedEvents);
        Assert.Equal("delay-500ms", s.HoverPreview);
        Assert.True(s.TintedEvents && s.FreeScroll && s.TasksEnabled && s.ShowTasksOnCalendar && s.InvitationParsing);
        Assert.False(s.ShowWeekNumbers || s.HideNonWorkingDays || s.ShowTimeInMonthView || s.ShowTasksApp || s.ShowBirthdayCalendar || s.TwelveHour);
    }

    [Fact]
    public void The_time_zone_setting_is_auto_or_an_iana_name()
    {
        Assert.Equal("auto", CalendarSettings.Read(PrefsSnapshot.FromJson([])).TimeZone);
        Assert.Null(CalendarSettings.Read(PrefsSnapshot.FromJson([])).Zone());
        var berlin = CalendarSettings.Read(PrefsSnapshot.FromJson(new System.Text.Json.Nodes.JsonObject { ["timeZone"] = "Europe/Berlin" }));
        Assert.Equal("Europe/Berlin", berlin.TimeZone);
        Assert.NotNull(berlin.Zone());
        Assert.Null(CalendarSettings.Read(PrefsSnapshot.FromJson(new System.Text.Json.Nodes.JsonObject { ["timeZone"] = "Mars/Olympus" })).Zone());
    }
}
