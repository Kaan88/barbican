// Mirrors: Rust reminders.rs:351-533 (runner, check, act, missed_done); Bulwark hooks/use-calendar-alerts.ts. Contract from the M4 plan section 4.1; owner cal-data.

namespace Barbican.Core.Calendar.Reminders;

/// <summary>Reminders that came due now (<c>Missed</c> false) or were found missed after a start (true), for the popup and the notification service.</summary>
public sealed class ReminderEventArgs(string owner, IReadOnlyList<DueReminder> reminders, bool missed) : EventArgs
{
    /// <summary>The account key the reminders belong to.</summary>
    public string Owner { get; } = owner;

    public IReadOnlyList<DueReminder> Reminders { get; } = reminders;

    public bool Missed { get; } = missed;

    /// <summary>Whether the reminder may play a sound: <c>calendarNotificationSound</c> on and do-not-disturb off (the notification service of M7 and the popup follow it).</summary>
    public bool Sound { get; init; }
}
