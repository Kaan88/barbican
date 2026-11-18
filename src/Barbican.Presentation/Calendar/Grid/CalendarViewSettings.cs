// Mirrors: Rust calendar.rs:112-:187 CalPrefs/cal_prefs (calendarDayStartHour, calendarWorkingDays, calendarNarrowWeekAllDays on top of CalendarSettings), :151 hover_delay, :156 month_timed; B-CAL-055.

using System.Text.Json.Nodes;
using Barbican.Core.Calendar;
using Barbican.Core.Storage;

namespace Barbican.Presentation.Calendar.Grid;

/// <summary>What the grids read from the settings: <see cref="CalendarSettings"/> plus the three keys the Rust app also honours.</summary>
public sealed record CalendarViewSettings(CalendarSettings Core, int DayStartHour, IReadOnlyList<int> WorkingDays, bool NarrowAllDays)
{
    /// <summary>The defaults (Bulwark's).</summary>
    public static CalendarViewSettings Default { get; } = new(new CalendarSettings(), 8, [1, 2, 3, 4, 5], false);

    /// <summary>The first visible hour: the day start when the hours are limited, else 0.</summary>
    public int HourFrom => Core.LimitHours && Core.DayEndHour > DayStartHour ? Math.Clamp(DayStartHour, 0, 23) : 0;

    /// <summary>The hour after the last visible one: the day end when limited, else 24.</summary>
    public int HourTo => Core.LimitHours && Core.DayEndHour > DayStartHour ? Math.Clamp(Core.DayEndHour, 1, 24) : 24;

    /// <summary>The hover preview delay in ms; -1 is off (calendarHoverPreview).</summary>
    public int HoverDelayMs => Core.HoverPreview switch
    {
        "off" => -1,
        "instant" => 0,
        "delay-1s" => 1000,
        "delay-2s" => 2000,
        _ => 500,
    };

    /// <summary>How a timed event looks in the month (calendarMonthTimedEvents: "text" or "dot", else a bar).</summary>
    public MonthItemStyle MonthStyle => Core.MonthTimedEvents switch
    {
        "text" => MonthItemStyle.Text,
        "dot" => MonthItemStyle.Dot,
        _ => MonthItemStyle.Bar,
    };

    public static CalendarViewSettings Read(PrefsSnapshot prefs)
    {
        ArgumentNullException.ThrowIfNull(prefs);
        var working = prefs.Values["calendarWorkingDays"] is JsonArray a
            ? a.Select(n => n is JsonValue v && v.TryGetValue<int>(out var d) ? ((d % 7) + 7) % 7 : -1).Where(d => d >= 0).ToList()
            : [1, 2, 3, 4, 5];
        return new CalendarViewSettings(
            CalendarSettings.Read(prefs),
            (int)Math.Clamp(prefs.Number("calendarDayStartHour", 8), 0, 23),
            working,
            prefs.Bool("calendarNarrowWeekAllDays", false));
    }
}
