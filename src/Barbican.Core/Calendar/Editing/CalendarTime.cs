// Mirrors: Rust recurrence.rs:18 parse_local, :24 fmt_local, :27 duration_secs, :52 duration_string (the small text helpers the editing rules need on their own; the recurrence engine of cal-data owns the same
// conversions for expansion). RFC 8984 1.4.4 (LocalDateTime), RFC 5545 3.3.6 (durations).

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Barbican.Core.Calendar.Editing;

/// <summary>Wall-clock text and ISO 8601 durations, as JSCalendar writes them.</summary>
internal static class CalendarTime
{
    private const string LocalFormat = "yyyy-MM-dd'T'HH:mm:ss";

    /// <summary>"2026-11-20T10:00:00" (a trailing Z or a fraction is ignored; a bare date is midnight).</summary>
    public static DateTime? ParseLocal(string? text)
    {
        if (text is null)
        {
            return null;
        }

        var s = text.Trim().TrimEnd('Z');
        if (s.Length >= 19 && DateTime.TryParseExact(s[..19], LocalFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
        {
            return t;
        }

        return s.Length >= 10 && DateTime.TryParseExact(s[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    public static string FormatLocal(DateTime value) => value.ToString(LocalFormat, CultureInfo.InvariantCulture);

    /// <summary>"PnW", "PnDTnHnMnS" with an optional sign, in seconds; null when it is not a duration.</summary>
    public static long? DurationSeconds(string? text)
    {
        if (text is null)
        {
            return null;
        }

        var s = text.Trim();
        var negative = false;
        if (s.StartsWith('-'))
        {
            negative = true;
            s = s[1..];
        }
        else if (s.StartsWith('+'))
        {
            s = s[1..];
        }

        if (!s.StartsWith('P'))
        {
            return null;
        }

        long total = 0;
        var number = "";
        var inTime = false;
        var any = false;
        foreach (var ch in s[1..])
        {
            if (ch == 'T')
            {
                inTime = true;
            }
            else if (ch is >= '0' and <= '9')
            {
                number += ch;
            }
            else
            {
                if (!long.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
                {
                    return null;
                }

                number = "";
                any = true;
                switch (ch, inTime)
                {
                    case ('W', false): total += n * 7 * 86400; break;
                    case ('D', false): total += n * 86400; break;
                    case ('H', true): total += n * 3600; break;
                    case ('M', true): total += n * 60; break;
                    case ('S', true): total += n; break;
                    default: return null;
                }
            }
        }

        return number.Length > 0 || !any ? null : negative ? -total : total;
    }

    /// <summary>"P1D", "PT1H30M", "P2DT3H", "PT0S".</summary>
    public static string DurationString(long seconds)
    {
        var secs = Math.Max(0, seconds);
        long d = secs / 86400, h = secs % 86400 / 3600, m = secs % 3600 / 60, s = secs % 60;
        var o = new StringBuilder("P");
        if (d > 0)
        {
            o.Append(CultureInfo.InvariantCulture, $"{d}D");
        }

        if (h > 0 || m > 0 || s > 0)
        {
            o.Append('T');
            if (h > 0)
            {
                o.Append(CultureInfo.InvariantCulture, $"{h}H");
            }

            if (m > 0)
            {
                o.Append(CultureInfo.InvariantCulture, $"{m}M");
            }

            if (s > 0)
            {
                o.Append(CultureInfo.InvariantCulture, $"{s}S");
            }
        }

        return o.Length == 1 ? "PT0S" : o.ToString();
    }

    /// <summary>The event's length as the grid reads it: a missing or unreadable duration is an hour (a day when all-day); a zero or negative all-day length is a day.</summary>
    public static TimeSpan EventDuration(JsonNode ev)
    {
        var allDay = (bool?)ev["showWithoutTime"] == true;
        var secs = DurationSeconds((string?)ev["duration"]) ?? (allDay ? 86400 : 3600);
        return TimeSpan.FromSeconds(secs <= 0 && allDay ? 86400 : Math.Max(0, secs));
    }
}
