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
    [Fact]
    public void Abbreviation_search_finds_reference_zones_without_changing_their_ids()
    {
        Assert.True(Barbican.Core.Settings.Pages.SettingTimeZoneSearchAliases.Matches("cest", "Europe/Berlin"));
        Assert.True(Barbican.Core.Settings.Pages.SettingTimeZoneSearchAliases.Matches("CST", "Asia/Shanghai"));
        Assert.True(Barbican.Core.Settings.Pages.SettingTimeZoneSearchAliases.Matches("PDT", "America/Los_Angeles"));
        Assert.False(Barbican.Core.Settings.Pages.SettingTimeZoneSearchAliases.Matches("CET", "Asia/Tokyo"));
    }

    [Fact]
    public void Display_zone_auto_unknown_and_dst_use_explicit_fallback()
    {
        var fallback = TimeZoneInfo.Utc;
        Assert.Same(fallback, Barbican.Core.Settings.Pages.SettingDisplayTimeZone.Resolve("auto", fallback));
        Assert.Same(fallback, Barbican.Core.Settings.Pages.SettingDisplayTimeZone.Resolve("Mars/Olympus", fallback));
        Assert.False(Barbican.Core.Settings.Pages.SettingDisplayTimeZone.TryResolve("Mars/Olympus", out _));
        var berlin = Barbican.Core.Settings.Pages.SettingDisplayTimeZone.Resolve("europe/berlin", fallback);
        var winter = new DateTimeOffset(2026, 3, 29, 0, 30, 0, TimeSpan.Zero);
        var summer = winter.AddHours(1);
        Assert.Equal(1, TimeZoneInfo.ConvertTime(winter, berlin).Hour);
        Assert.Equal(3, TimeZoneInfo.ConvertTime(summer, berlin).Hour);
        Assert.Equal("Europe/Berlin", Barbican.Core.Settings.Pages.SettingDisplayTimeZone.EventZone("europe/berlin", fallback));
        Assert.Equal("UTC", Barbican.Core.Settings.Pages.SettingDisplayTimeZone.EventZone("Mars/Olympus", fallback));
    }

}
