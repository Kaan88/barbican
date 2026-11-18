// Mirrors: IReminderPopup. Stub from the M4 plan section 4.1; owner cal-data replaces the class (constructor dependencies: IReminderScheduler, IShellNavigation (ShellViewModel), ICalendarViewModel, IPrefsStore, IDispatcher).

using System.ComponentModel;
using Barbican.Core.Calendar.Reminders;
using Barbican.Core.Common;

namespace Barbican.Presentation.Calendar.Reminders;

/// <summary>The default <see cref="IReminderPopup"/>.</summary>
public sealed class ReminderPopup : IReminderPopup
{
    public event PropertyChangedEventHandler? PropertyChanged
    {
        add { }
        remove { }
    }

    public IReadOnlyList<DueReminder> Shown => throw new PortPendingException("ReminderPopup.Shown");

    public IReadOnlyList<DueReminder> Missed => throw new PortPendingException("ReminderPopup.Missed");

    public Task DismissAsync(DueReminder reminder, CancellationToken ct) => throw new PortPendingException("ReminderPopup.DismissAsync");

    public Task DismissAllAsync(CancellationToken ct) => throw new PortPendingException("ReminderPopup.DismissAllAsync");

    public void Snooze(DueReminder reminder, int minutes) => throw new PortPendingException("ReminderPopup.Snooze");

    public Task OpenAsync(DueReminder reminder, CancellationToken ct) => throw new PortPendingException("ReminderPopup.OpenAsync");

    public Task LoadMissedAsync(CancellationToken ct) => throw new PortPendingException("ReminderPopup.LoadMissedAsync");
}
