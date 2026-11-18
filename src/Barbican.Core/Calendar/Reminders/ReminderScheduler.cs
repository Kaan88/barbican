// Mirrors: Rust reminders.rs:351-533. Stub from the M4 plan section 4.1; owner cal-data replaces the class (constructor dependencies: IAccountManager, ICalendarStore, IReminderStateStore, IHookBus, TimeProvider, IPrefsStore).

using Barbican.Core.Common;

namespace Barbican.Core.Calendar.Reminders;

/// <summary>The default <see cref="IReminderScheduler"/>.</summary>
public sealed class ReminderScheduler : IReminderScheduler
{
    public event EventHandler<ReminderEventArgs>? Due
    {
        add { }
        remove { }
    }

    public void Start() => throw new PortPendingException("ReminderScheduler.Start");

    public void Shutdown() => throw new PortPendingException("ReminderScheduler.Shutdown");

    public Task DismissAsync(IReadOnlyList<DueReminder> reminders, CancellationToken ct) => throw new PortPendingException("ReminderScheduler.DismissAsync");

    public void Snooze(IReadOnlyList<DueReminder> reminders, int minutes) => throw new PortPendingException("ReminderScheduler.Snooze");

    public Task<IReadOnlyList<DueReminder>> MissedAsync(CancellationToken ct) => throw new PortPendingException("ReminderScheduler.MissedAsync");
}
