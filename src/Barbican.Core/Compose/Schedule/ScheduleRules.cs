// Mirrors: Rust schedule.rs:26 presets, :40 parse_local, :64 validate, :77 label; Bulwark lib/schedule-presets.ts, components/email/schedule-send-dialog.tsx, lib/scheduled-send-label.ts; RFC 4865 (FUTURERELEASE).

using System.Globalization;
using Barbican.Core.Common;
using Barbican.Core.Jmap;

namespace Barbican.Core.Compose.Schedule;

/// <summary>The schedule dialog's pure rules. Times are local to <c>zone</c> (the app passes <c>TimeProvider.LocalTimeZone</c>, D-242).</summary>
public static class ScheduleRules
{
    private static readonly string[] DateFormats = ["yyyy-M-d", "d.M.yyyy", "d/M/yyyy"];

    /// <summary>How far ahead a time for today must be to be offered (5 minutes).</summary>
    public static TimeSpan TodayLead { get; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// One-click times: today 8:00 ("This morning") and 13:00 ("This afternoon") while at least <see cref="TodayLead"/> ahead, tomorrow 8:00 and 13:00 ("Tomorrow morning", "Tomorrow afternoon"), and
    /// next Monday 8:00 ("Monday morning") unless that is tomorrow.
    /// </summary>
    public static IReadOnlyList<SchedulePreset> Presets(DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var local = TimeZoneInfo.ConvertTime(now, zone);
        var list = new List<SchedulePreset>();
        foreach (var (label, hour) in new[] { ("This morning", 8), ("This afternoon", 13) })
        {
            if (At(local, 0, hour, zone) is { } d && d - now >= TodayLead)
            {
                list.Add(new SchedulePreset(label, d));
            }
        }

        foreach (var (label, hour) in new[] { ("Tomorrow morning", 8), ("Tomorrow afternoon", 13) })
        {
            if (At(local, 1, hour, zone) is { } d)
            {
                list.Add(new SchedulePreset(label, d));
            }
        }

        var dow = (int)local.DayOfWeek;
        var toMonday = (8 - dow) % 7;
        if (toMonday == 0)
        {
            toMonday = 7;
        }

        if (toMonday > 1 && At(local, toMonday, 8, zone) is { } monday)
        {
            list.Add(new SchedulePreset("Monday morning", monday));
        }

        return list;
    }

    /// <summary>Parses the dialog's date (2026-11-17, 17.11.2026, 17/11/2026) and time (08:00, 8:30 pm, 12 am, 20.15) as local time; null when either does not parse (hour 25, 30 February).</summary>
    public static DateTimeOffset? ParseLocal(string date, string time, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(date);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(zone);
        if (!DateTime.TryParseExact(date.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
        {
            return null;
        }

        var t = time.Trim().ToLowerInvariant().Replace('.', ':');
        var pm = t.EndsWith("pm", StringComparison.Ordinal);
        var am = t.EndsWith("am", StringComparison.Ordinal);
        var clock = pm || am ? t[..^2].Trim() : t;
        int h;
        int m;
        var colon = clock.IndexOf(':', StringComparison.Ordinal);
        if (colon >= 0)
        {
            if (!TryNumber(clock[..colon], out h) || !TryNumber(clock[(colon + 1)..], out m))
            {
                return null;
            }
        }
        else
        {
            if (!TryNumber(clock, out h))
            {
                return null;
            }

            m = 0;
        }

        if (pm || am)
        {
            if (h is < 1 or > 12)
            {
                return null;
            }

            h = (h % 12) + (pm ? 12 : 0);
        }

        if (h > 23 || m > 59)
        {
            return null;
        }

        return ToOffset(new DateTime(d.Year, d.Month, d.Day, h, m, 0, DateTimeKind.Unspecified), zone);
    }

    /// <summary>
    /// Bulwark's checks before a time is taken; <paramref name="maxSeconds"/> is maxDelayedSend (0: no hold). Failures are <see cref="ScheduleError"/>s with the dialog's messages: "Scheduled send is not supported for this account.",
    /// "Choose a date and time." (not <paramref name="filled"/>), "Enter a valid date and time." (null), "Choose a future date and time.", "This time is later than the server allows.".
    /// </summary>
    public static Result<DateTimeOffset> Validate(DateTimeOffset? when, DateTimeOffset now, long maxSeconds, bool filled)
    {
        if (maxSeconds == 0)
        {
            return Result<DateTimeOffset>.Fail(new ScheduleError("Scheduled send is not supported for this account."));
        }

        if (!filled)
        {
            return Result<DateTimeOffset>.Fail(new ScheduleError("Choose a date and time."));
        }

        if (when is not { } w)
        {
            return Result<DateTimeOffset>.Fail(new ScheduleError("Enter a valid date and time."));
        }

        if (w <= now)
        {
            return Result<DateTimeOffset>.Fail(new ScheduleError("Choose a future date and time."));
        }

        return (long)(w - now).TotalSeconds > maxSeconds
            ? Result<DateTimeOffset>.Fail(new ScheduleError("This time is later than the server allows."))
            : Result<DateTimeOffset>.Ok(w);
    }

    /// <summary>The release time for lists, banners and toasts (Bulwark formatScheduledSendAt): rounded to the minute (the server can land a second early), with the date ("Tue 17 Nov, 08:00") unless it is today; 12-hour form "1:00 PM" when asked.</summary>
    public static string Label(DateTimeOffset when, bool twelveHour, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var local = TimeZoneInfo.ConvertTime(when, zone);
        var secs = when.ToUnixTimeSeconds() + (local.Second >= 30 ? 60 : 0);
        var rounded = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(secs - (((secs % 60) + 60) % 60)), zone);
        var time = rounded.ToString(twelveHour ? "h:mm tt" : "HH:mm", CultureInfo.InvariantCulture);
        return rounded.Date == TimeZoneInfo.ConvertTime(now, zone).Date ? time : rounded.ToString("ddd d MMM", CultureInfo.InvariantCulture) + ", " + time;
    }

    private static bool TryNumber(string s, out int value) => int.TryParse(s.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value);

    private static DateTimeOffset? At(DateTimeOffset local, int days, int hour, TimeZoneInfo zone) =>
        ToOffset(local.Date.AddDays(days).AddHours(hour), zone);

    /// <summary>A wall-clock time in the zone as an instant; the earlier instant when the time is ambiguous, null when it does not exist (spring forward).</summary>
    private static DateTimeOffset? ToOffset(DateTime wall, TimeZoneInfo zone)
    {
        var unspecified = DateTime.SpecifyKind(wall, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(unspecified))
        {
            return null;
        }

        var offset = zone.IsAmbiguousTime(unspecified) ? zone.GetAmbiguousTimeOffsets(unspecified).Max() : zone.GetUtcOffset(unspecified);
        return new DateTimeOffset(unspecified, offset);
    }
}
