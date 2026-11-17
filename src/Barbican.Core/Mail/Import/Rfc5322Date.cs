// Mirrors: RFC 5322 section 3.3 (date-time: [day-of-week ","] day month year hour ":" minute [":" second] zone) and section 4.3 (obsolete zones UT, GMT,
// EST..PDT); Rust mailimport.rs:148 received_at (chrono parse_from_rfc2822). Owner unified (M2 wave 2).

using System.Globalization;

namespace Barbican.Core.Mail.Import;

/// <summary>Parses an RFC 5322 date as mail programs write it into the Date header.</summary>
internal static class Rfc5322Date
{
    private static readonly string[] Months = ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];

    /// <summary>Reads "Tue, 05 May 2020 10:11:12 +0200" (day of week and seconds optional, obsolete zone names accepted).</summary>
    public static bool TryParse(string text, out DateTimeOffset value)
    {
        value = default;
        var words = text.Replace(",", " ", StringComparison.Ordinal).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();
        if (words.Count > 0 && words[0].Length == 3 && !char.IsAsciiDigit(words[0][0]))
        {
            words.RemoveAt(0);
        }

        if (words.Count < 5
            || !int.TryParse(words[0], NumberStyles.None, CultureInfo.InvariantCulture, out var day)
            || Array.IndexOf(Months, words[1].ToLowerInvariant()) is var month && month < 0
            || !int.TryParse(words[2], NumberStyles.None, CultureInfo.InvariantCulture, out var year))
        {
            return false;
        }

        // RFC 5322 section 4.3: a two-digit year is 1950-2049, a three-digit one adds 1900
        year = year switch
        {
            < 50 when words[2].Length <= 2 => year + 2000,
            < 100 when words[2].Length <= 2 => year + 1900,
            < 1000 => year + 1900,
            _ => year,
        };
        var time = words[3].Split(':');
        if (time.Length is < 2 or > 3
            || !int.TryParse(time[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hour)
            || !int.TryParse(time[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minute))
        {
            return false;
        }

        var second = 0;
        if (time.Length == 3 && !int.TryParse(time[2], NumberStyles.None, CultureInfo.InvariantCulture, out second))
        {
            return false;
        }

        if (!TryZone(words[4], out var offset))
        {
            return false;
        }

        try
        {
            value = new DateTimeOffset(year, month + 1, day, hour, minute, Math.Min(second, 59), offset);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static bool TryZone(string zone, out TimeSpan offset)
    {
        offset = TimeSpan.Zero;
        if (zone.Length == 5 && zone[0] is '+' or '-' && int.TryParse(zone.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var hhmm))
        {
            var minutes = (hhmm / 100 * 60) + (hhmm % 100);
            if (hhmm % 100 >= 60 || minutes >= 24 * 60)
            {
                return false;
            }

            offset = TimeSpan.FromMinutes(zone[0] == '-' ? -minutes : minutes);
            return true;
        }

        int? hours = zone.ToUpperInvariant() switch
        {
            "UT" or "GMT" or "Z" => 0,
            "EDT" => -4,
            "EST" or "CDT" => -5,
            "CST" or "MDT" => -6,
            "MST" or "PDT" => -7,
            "PST" => -8,

            // RFC 5322 section 4.3: military zones carry no reliable meaning and are read as -0000
            { Length: 1 } z when char.IsAsciiLetter(z[0]) => 0,
            _ => null,
        };
        if (hours is null)
        {
            return false;
        }

        offset = TimeSpan.FromHours(hours.Value);
        return true;
    }
}
