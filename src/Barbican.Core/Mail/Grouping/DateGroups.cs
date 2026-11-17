// Mirrors: Bulwark lib/list-date-groups.ts:21-44 (listDateGroup, sectionThreadsByDate). Owner mailcore.

namespace Barbican.Core.Mail.Grouping;

/// <summary>Which date group a message goes under, comparing calendar days in a time zone.</summary>
public static class DateGroups
{
    /// <summary>
    /// The group of <paramref name="date"/> as seen at <paramref name="now"/> in <paramref name="zone"/>. A date in the future is Today.
    /// Last month reaches back to the same day of the month before (its last day when that month is shorter).
    /// </summary>
    public static ListDateGroup GroupOf(DateTimeOffset date, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var today = DayOf(now, zone);
        var day = DayOf(date, zone);
        var daysAgo = today.DayNumber - day.DayNumber;
        if (daysAgo <= 0)
        {
            return ListDateGroup.Today;
        }

        if (daysAgo == 1)
        {
            return ListDateGroup.Yesterday;
        }

        if (daysAgo <= 6)
        {
            return ListDateGroup.OnWeekday(day.DayOfWeek);
        }

        if (daysAgo <= 13)
        {
            return ListDateGroup.LastWeek;
        }

        var firstOfMonth = new DateOnly(today.Year, today.Month, 1);
        var monthBack = firstOfMonth.AddMonths(-1);
        var monthBackDay = Math.Min(today.Day, DateTime.DaysInMonth(monthBack.Year, monthBack.Month));
        var cutoff = new DateOnly(monthBack.Year, monthBack.Month, monthBackDay);
        return day.DayNumber >= cutoff.DayNumber ? ListDateGroup.LastMonth : ListDateGroup.Older;
    }

    /// <summary>The calendar day <paramref name="date"/> falls on in <paramref name="zone"/>.</summary>
    public static DateOnly DayOf(DateTimeOffset date, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(date, zone).DateTime);
    }
}
