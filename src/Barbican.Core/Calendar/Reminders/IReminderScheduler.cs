// Mirrors: Rust reminders.rs:351 start, :366 stop, :373 fetch, :412 check, :499 act, :523 missed_done; Bulwark hooks/use-calendar-alerts.ts (poll, snooze, acknowledge, stale threshold), spec B-CAL-028..035. Contract from the M4 plan section 4.1; owner cal-data.

namespace Barbican.Core.Calendar.Reminders;

/// <summary>
/// Fires reminders for every signed-in login (not only the active one): reads events <see cref="ReminderScheduler.OwnAlertHorizon"/> (a year) ahead, so an event's own alert fires however far off the event is, refetches every 300 s and on a calendar change, checks every
/// 15 s on <see cref="TimeProvider"/>, and raises <see cref="Due"/> once per key. Shown reminders are not repeated after Dismiss or Snooze; calendar notifications and sound follow the settings (the notification
/// service of M7 subscribes; until then the popup of cal-ui does).
/// </summary>
public interface IReminderScheduler
{
    event EventHandler<ReminderEventArgs>? Due;

    /// <summary>Starts or restarts after a sign-in; idempotent.</summary>
    void Start();

    void Shutdown();

    /// <summary>Dismiss: acknowledged keys are stored and never shown again.</summary>
    Task DismissAsync(IReadOnlyList<DueReminder> reminders, CancellationToken ct);

    /// <summary>Snooze: shown again after <paramref name="minutes"/> (kept in memory only).</summary>
    void Snooze(IReadOnlyList<DueReminder> reminders, int minutes);

    /// <summary>The missed reminders of every login (the dialog after a start); empty after <see cref="DismissAsync"/>.</summary>
    Task<IReadOnlyList<DueReminder>> MissedAsync(CancellationToken ct);
}
