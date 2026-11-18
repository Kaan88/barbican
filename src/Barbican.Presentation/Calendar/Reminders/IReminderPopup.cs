// Mirrors: Rust reminders.rs:412 check, :499 act, :523 missed_done (the popup with Dismiss, Snooze and Open, the missed-reminders dialog); Bulwark components/calendar/missed-reminders-dialog.tsx, hooks/use-calendar-alerts.ts; I-338, B-CAL-033, 035.
// Contract from the M4 plan section 4.1; owner cal-data writes the view model, cal-ui draws the popup and the dialog (a view over this interface).

using System.ComponentModel;
using Barbican.Core.Calendar.Reminders;

namespace Barbican.Presentation.Calendar.Reminders;

/// <summary>The reminders on screen and the missed-reminders dialog. Shown reminders come from <c>IReminderScheduler.Due</c>; the notification service (M7) and the sound follow <c>calendarNotificationsEnabled</c> and <c>calendarNotificationSound</c>.</summary>
public interface IReminderPopup : INotifyPropertyChanged
{
    /// <summary>Reminders waiting for an answer, soonest first; the popup shows while there are any.</summary>
    IReadOnlyList<DueReminder> Shown { get; }

    /// <summary>The dialog's list after a start (null or empty: no dialog).</summary>
    IReadOnlyList<DueReminder> Missed { get; }

    Task DismissAsync(DueReminder reminder, CancellationToken ct);

    Task DismissAllAsync(CancellationToken ct);

    /// <summary>Snooze by 5, 10, 15 or 60 minutes.</summary>
    void Snooze(DueReminder reminder, int minutes);

    /// <summary>Opens the reminder's event or task in the calendar (switches the app and selects it).</summary>
    Task OpenAsync(DueReminder reminder, CancellationToken ct);

    /// <summary>Loads the missed list once after a start (the dialog opens when it is not empty).</summary>
    Task LoadMissedAsync(CancellationToken ct);
}
