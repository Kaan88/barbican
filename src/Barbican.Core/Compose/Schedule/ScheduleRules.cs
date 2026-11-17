// Mirrors: Rust schedule.rs:26 presets, :40 parse_local, :64 validate, :77 label; Bulwark lib/schedule-presets.ts, components/email/schedule-send-dialog.tsx, lib/scheduled-send-label.ts; RFC 4865. Contract stub from the M3 plan section 4.3; owner send-pipeline replaces the bodies.

using Barbican.Core.Common;

namespace Barbican.Core.Compose.Schedule;

/// <summary>The schedule dialog's pure rules. Times are local to <c>zone</c> (the app passes <c>TimeProvider.LocalTimeZone</c>). Stub until stream send-pipeline lands.</summary>
public static class ScheduleRules
{
    /// <summary>How far ahead a time for today must be to be offered (5 minutes).</summary>
    public static TimeSpan TodayLead { get; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// One-click times: today 8:00 ("This morning") and 13:00 ("This afternoon") while at least <see cref="TodayLead"/> ahead, tomorrow 8:00 and 13:00 ("Tomorrow morning", "Tomorrow afternoon"), and
    /// next Monday 8:00 ("Monday morning") unless that is tomorrow.
    /// </summary>
    public static IReadOnlyList<SchedulePreset> Presets(DateTimeOffset now, TimeZoneInfo zone) => throw new PortPendingException("ScheduleRules.Presets");

    /// <summary>Parses the dialog's date (2026-11-17, 17.11.2026, 17/11/2026) and time (08:00, 8:30 pm, 12 am, 20.15) as local time; null when either does not parse (hour 25, 30 February).</summary>
    public static DateTimeOffset? ParseLocal(string date, string time, TimeZoneInfo zone) => throw new PortPendingException("ScheduleRules.ParseLocal");

    /// <summary>
    /// Bulwark's checks before a time is taken; <paramref name="maxSeconds"/> is maxDelayedSend (0: no hold). Failures are <see cref="Jmap.ScheduleError"/>s with the dialog's messages: "Scheduled send is not supported for this account.",
    /// "Choose a date and time." (not <paramref name="filled"/>), "Enter a valid date and time." (null), "Choose a future date and time.", "This time is later than the server allows.".
    /// </summary>
    public static Result<DateTimeOffset> Validate(DateTimeOffset? when, DateTimeOffset now, long maxSeconds, bool filled) => throw new PortPendingException("ScheduleRules.Validate");

    /// <summary>The release time for lists, banners and toasts (Bulwark formatScheduledSendAt): rounded to the minute (the server can land a second early), with the date ("Tue 17 Nov, 08:00") unless it is today; 12-hour form "1:00 PM" when asked.</summary>
    public static string Label(DateTimeOffset when, bool twelveHour, DateTimeOffset now, TimeZoneInfo zone) => throw new PortPendingException("ScheduleRules.Label");
}
