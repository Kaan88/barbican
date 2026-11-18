// Mirrors: Rust calendar.rs:160 cal_prefs, :146 week_days_shown, :151 hover_delay, :156 month_timed; spec/settings.csv rows S-calendar*, S-showWeekNumbers, S-firstDayOfWeek, S-showTimeInMonthView, S-enableCalendarTasks, S-showTasksOnCalendar, S-showTasksApp,
// S-showBirthdayCalendar, S-timeFormat, S-timeZone; B-CAL-055. Real from the M4 plan section 4.2 (the defaults are Rust's); owner cal-data keeps it in step with settings.csv.

using Barbican.Core.Storage;

namespace Barbican.Core.Calendar;

/// <summary>The calendar settings, read when a view opens (the defaults for anything not stored).</summary>
public sealed record CalendarSettings
{
    /// <summary>0 Sunday, 1 Monday.</summary>
    public int FirstDayOfWeek { get; init; } = 1;

    public bool ShowWeekNumbers { get; init; }

    public bool LimitHours { get; init; } = true;

    /// <summary>The last visible hour when <see cref="LimitHours"/> is on.</summary>
    public int DayEndHour { get; init; } = 20;

    public bool HideNonWorkingDays { get; init; }

    public bool ShowTimeInMonthView { get; init; }

    /// <summary>"bar" or "dot" (S-calendarMonthTimedEvents).</summary>
    public string MonthTimedEvents { get; init; } = "bar";

    public bool TintedEvents { get; init; } = true;

    public bool FreeScroll { get; init; } = true;

    /// <summary>"delay-500ms", "instant" or "off".</summary>
    public string HoverPreview { get; init; } = "delay-500ms";

    public bool TasksEnabled { get; init; } = true;

    public bool ShowTasksOnCalendar { get; init; } = true;

    public bool ShowTasksApp { get; init; }

    public bool ShowBirthdayCalendar { get; init; }

    public bool TwelveHour { get; init; }

    public bool InvitationParsing { get; init; } = true;

    public bool NotificationsEnabled { get; init; } = true;

    public bool NotificationSound { get; init; } = true;

    /// <summary>S-timeZone: "auto" (the machine's zone) or an IANA name.</summary>
    public string TimeZone { get; init; } = "auto";

    /// <summary>The user's zone for floating times and the views: null for "auto" or a name this machine does not know (the machine's zone then).</summary>
    public TimeZoneInfo? Zone()
    {
        if (string.IsNullOrWhiteSpace(TimeZone) || string.Equals(TimeZone, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(TimeZone);
        }
        catch (TimeZoneNotFoundException)
        {
            return null;
        }
        catch (InvalidTimeZoneException)
        {
            return null;
        }
    }

    public static CalendarSettings Read(PrefsSnapshot prefs)
    {
        ArgumentNullException.ThrowIfNull(prefs);
        return new CalendarSettings
        {
            FirstDayOfWeek = (int)prefs.Number("firstDayOfWeek", 1),
            ShowWeekNumbers = prefs.Bool("showWeekNumbers", false),
            LimitHours = prefs.Bool("calendarLimitHours", true),
            DayEndHour = (int)prefs.Number("calendarDayEndHour", 20),
            HideNonWorkingDays = prefs.Bool("calendarHideNonWorkingDays", false),
            ShowTimeInMonthView = prefs.Bool("showTimeInMonthView", false),
            MonthTimedEvents = prefs.Str("calendarMonthTimedEvents", "bar"),
            TintedEvents = prefs.Bool("calendarTintedEvents", true),
            FreeScroll = prefs.Bool("calendarFreeScroll", true),
            HoverPreview = prefs.Str("calendarHoverPreview", "delay-500ms"),
            TasksEnabled = prefs.Bool("enableCalendarTasks", true),
            ShowTasksOnCalendar = prefs.Bool("showTasksOnCalendar", true),
            ShowTasksApp = prefs.Bool("showTasksApp", false),
            ShowBirthdayCalendar = prefs.Bool("showBirthdayCalendar", false),
            TwelveHour = string.Equals(prefs.Str("timeFormat", "24h"), "12h", StringComparison.Ordinal),
            InvitationParsing = prefs.Bool("calendarInvitationParsingEnabled", true),
            NotificationsEnabled = prefs.Bool("calendarNotificationsEnabled", true),
            NotificationSound = prefs.Bool("calendarNotificationSound", true),
            TimeZone = prefs.Str("timeZone", "auto"),
        };
    }
}
