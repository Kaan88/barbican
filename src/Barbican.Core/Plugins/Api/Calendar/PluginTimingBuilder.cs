// Mirrors: Bulwark lib/plugin-sandbox/host-api-calendar.ts:47-49 (MAX_PLUGIN_EVENT_RANGE_DAYS), :102-195 (readTimeZone, readDurationSeconds, formatDuration, buildTiming).
// Wall-clock times are stored in their zone (JSCalendar start + timeZone, RFC 8984 4.2.1, 4.7.1); a Z time is stored in Etc/UTC; an all-day event has no zone and lasts whole days.
using System.Globalization;
using Barbican.Core.Calendar.Recurrence;

namespace Barbican.Core.Plugins.Api.Calendar;

internal static class PluginTimingBuilder
{
    /// <summary>The longest range calendar.getEvents covers and the longest an event may last (host-api-calendar.ts:48).</summary>
    internal const int MaxRangeDays = 366;

    private const long Day = 86_400;
    private const long MaxSeconds = MaxRangeDays * Day;

    /// <summary>A known IANA zone of 1-64 characters (readTimeZone, :102-110).</summary>
    internal static bool IsKnownZone(string? zone) => zone is { Length: > 0 and <= 64 } && TimeZones.ZoneOf(zone) is not null;

    /// <summary>A positive ISO 8601 duration (readDurationSeconds, :112-119).</summary>
    internal static bool TryDuration(string? text, out long seconds)
    {
        seconds = 0;
        if (text is null || RecurrenceEngine.DurationSeconds(text) is not { } s || s <= 0) return false;
        seconds = s;
        return true;
    }

    /// <summary>host-api-calendar.ts:140-195: false for anything buildTiming throws on.</summary>
    internal static bool TryBuild(PluginTimingInput input, string defaultZone, out PluginTiming timing)
    {
        timing = null!;
        if (!PluginWhenParser.TryRead(input.Start, out var start) || (input.End is not null && input.Duration is not null)) return false;
        var built = (input.IsAllDay ?? start.Time is null) ? AllDay(input, start) : Timed(input, start, defaultZone);
        if (built is null) return false;
        timing = built;
        return true;
    }

    /// <summary>:151-167: whole days, end exclusive, no zone.</summary>
    private static PluginTiming? AllDay(PluginTimingInput input, PluginWhen start)
    {
        if (input.TimeZone is not null) return null;
        long days = 1;
        if (input.End is not null)
        {
            if (!PluginWhenParser.TryRead(input.End, out var end)) return null;
            days = (long)Math.Round((end.Wall.Date - start.Wall.Date).TotalDays);
            if (days < 1) return null;
        }
        else if (input.Duration is not null)
        {
            if (!TryDuration(input.Duration, out var seconds) || seconds % Day != 0) return null;
            days = seconds / Day;
        }

        return days * Day > MaxSeconds ? null : new PluginTiming(start.Date + "T00:00:00", string.Create(CultureInfo.InvariantCulture, $"P{days}D"), null, true);
    }

    /// <summary>:169-194: a wall clock in its zone, or UTC when written with Z; one hour unless an end or a duration says otherwise.</summary>
    private static PluginTiming? Timed(PluginTimingInput input, PluginWhen start, string defaultZone)
    {
        if (start.Time is null) return null;
        string zone;
        if (start.Utc)
        {
            if (input.TimeZone is not null and not "Etc/UTC" and not "UTC") return null;
            zone = "Etc/UTC";
        }
        else if (input.TimeZone is null) zone = defaultZone;
        else if (IsKnownZone(input.TimeZone)) zone = input.TimeZone;
        else return null;

        long length = 3600;
        if (input.End is not null)
        {
            if (!PluginWhenParser.TryRead(input.End, out var end) || end.Time is null || end.Utc != start.Utc) return null;
            length = (end.WallMs - start.WallMs) / 1000;
            if (length <= 0) return null;
        }
        else if (input.Duration is not null && !TryDuration(input.Duration, out length)) return null;

        return length > MaxSeconds ? null : new PluginTiming(start.Date + "T" + start.Time, RecurrenceEngine.DurationString(length), zone, false);
    }
}
