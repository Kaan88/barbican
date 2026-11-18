// Mirrors: Rust calendar.rs:195 fmt_time, :466-:489 titles (day "Wednesday 18 November" with "Week N", week "16 – 22 November", agenda "Nov 18 – Dec 17, 2026"), cal_extra.rs agenda headings; B-CAL-053, 056.

using System.Globalization;

namespace Barbican.Presentation.Calendar.Grid;

/// <summary>The words and times of the calendar surface (invariant English as Bulwark's default, 24 or 12-hour clock by the setting).</summary>
public static class CalendarFormat
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    /// <summary>"14:30", or "2:30 PM" with the 12-hour clock.</summary>
    public static string Time(DateTime time, bool twelveHour) => time.ToString(twelveHour ? "h:mm tt" : "HH:mm", Culture);

    /// <summary>The hour label of the left gutter: "08:00" or "8 AM".</summary>
    public static string HourLabel(int hour, bool twelveHour)
    {
        var t = new DateTime(2000, 1, 1, hour % 24, 0, 0, DateTimeKind.Unspecified);
        return t.ToString(twelveHour ? "h tt" : "HH:mm", Culture);
    }

    /// <summary>"10:30–11:00" (the card's time line).</summary>
    public static string Range(DateTime start, DateTime end, bool twelveHour) => Time(start, twelveHour) + "–" + Time(end, twelveHour);

    /// <summary>The toolbar title of the view.</summary>
    public static string Title(CalendarViewKind view, DateOnly anchor, DateOnly from, DateOnly to, bool weekNumbers)
    {
        var last = to.AddDays(-1);
        switch (view)
        {
            case CalendarViewKind.Day:
                var t = anchor.ToString("dddd d MMMM", Culture);
                return weekNumbers ? t + "  ·  Week " + CalendarGridRules.WeekNumber(anchor).ToString(Culture) : t;
            case CalendarViewKind.Week:
                return from.Month == last.Month && from.Year == last.Year
                    ? from.Day.ToString(Culture) + " – " + last.ToString("d MMMM", Culture)
                    : from.ToString("d MMMM", Culture) + " – " + last.ToString("d MMMM", Culture);
            case CalendarViewKind.Agenda:
                return from.ToString("MMM d", Culture) + " – " + last.ToString("MMM d, yyyy", Culture);
            case CalendarViewKind.Tasks:
                return "Tasks";
            default:
                return anchor.ToString("MMMM yyyy", Culture);
        }
    }

    /// <summary>The smaller wording of a narrow header (B-CAL-053): "16 – 22 Nov".</summary>
    public static string ShortTitle(CalendarViewKind view, DateOnly anchor, DateOnly from, DateOnly to) => view switch
    {
        CalendarViewKind.Day => anchor.ToString("ddd d MMM", Culture),
        CalendarViewKind.Week => from.ToString("d MMM", Culture) + " – " + to.AddDays(-1).ToString("d MMM", Culture),
        CalendarViewKind.Month => anchor.ToString("MMM yyyy", Culture),
        _ => Title(view, anchor, from, to, false),
    };

    /// <summary>"Today", "Tomorrow", or "Wednesday, November 18" (the agenda's day heading).</summary>
    public static string DayHeading(DateOnly day, DateOnly today) =>
        day == today ? "Today" : day == today.AddDays(1) ? "Tomorrow" : day.ToString("dddd, MMMM d", Culture);

    /// <summary>"Wed 18 Nov · 14:00 – 15:30" or "Wed 18 Nov" (all day): the detail panel's and the hover card's when line (B-CAL-054).</summary>
    public static string When(DateTime start, DateTime end, bool allDay, bool twelveHour)
    {
        if (allDay)
        {
            var last = end > start ? end.AddDays(-1) : start;
            var first = start.ToString("ddd d MMM", Culture);
            return last.Date > start.Date ? first + " – " + last.ToString("ddd d MMM", Culture) : first;
        }

        var day = start.ToString("ddd d MMM", Culture);
        return end.Date == start.Date
            ? day + " · " + Time(start, twelveHour) + " – " + Time(end, twelveHour)
            : day + " " + Time(start, twelveHour) + " – " + end.ToString("ddd d MMM", Culture) + " " + Time(end, twelveHour);
    }
}
