// Mirrors: Rust contacts_x.rs:352 pretty_date; Bulwark lib/contact-dates.ts. B-CON-013; setting timeZone (the day "today" is in). Contract from the M5 plan section 4.1; owner con-data.

using System.Globalization;
using System.Text.Json.Nodes;

namespace Barbican.Core.Contacts;

/// <summary>Display and arithmetic of the dates of a card.</summary>
public static class ContactDates
{
    private static readonly string[] Months = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];

    /// <summary>English long form (d MMMM yyyy) and the days from <paramref name="today"/> to the next occurrence (today is 0).</summary>
    public static PrettyDate Pretty(JsonNode date, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(date);
        var t = ContactFields.DateText(date);
        var noYear = t.StartsWith("--", StringComparison.Ordinal);
        var parts = t.TrimStart('-').Split('-');
        int? Part(int i) => i < parts.Length && int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;
        int? y = noYear ? null : Part(0);
        var m = noYear ? Part(0) : Part(1);
        var d = noYear ? Part(1) : Part(2);
        var text = (y, m, d) switch
        {
            ({ } yy, { } mm, { } dd) when mm is >= 1 and <= 12 => $"{dd} {Months[mm - 1]} {yy}",
            (null, { } mm, { } dd) when mm is >= 1 and <= 12 => $"{dd} {Months[mm - 1]}",
            ({ } yy, { } mm, null) when mm is >= 1 and <= 12 => $"{Months[mm - 1]} {yy}",
            _ => t,
        };
        return new PrettyDate(text, m is { } mo && d is { } da ? DaysUntil(mo, da, today) : null);
    }

    /// <summary>The day a clock shows now in the zone of the timeZone setting ("auto" or an unknown name: this computer's zone).</summary>
    public static DateOnly Today(TimeProvider time, string? timeZone)
    {
        ArgumentNullException.ThrowIfNull(time);
        var zone = TimeZoneInfo.Local;
        if (!string.IsNullOrEmpty(timeZone) && !string.Equals(timeZone, "auto", StringComparison.Ordinal))
        {
            try
            {
                zone = TimeZoneInfo.FindSystemTimeZoneById(timeZone);
            }
            catch (TimeZoneNotFoundException)
            {
                // an unknown name: the local zone
            }
            catch (InvalidTimeZoneException)
            {
                // see above
            }
        }

        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), zone).DateTime);
    }

    private static bool Valid(int year, int month, int day) => month is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(year, month);

    private static int? DaysUntil(int month, int day, DateOnly today)
    {
        // an impossible date (a 29 February in a common year) falls on 1 March of that year (Rust fell back to a past date for the next year: fixed)
        DateOnly In(int year) => Valid(year, month, day) ? new DateOnly(year, month, day) : new DateOnly(year, 3, 1);
        var next = In(today.Year);
        if (next < today && today.Year < 9999)
        {
            next = In(today.Year + 1);
        }

        return next.DayNumber - today.DayNumber;
    }
}
