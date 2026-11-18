// Starts what keeps running behind the calendar once the window exists: the reminder scheduler (15 s checks, every login) and the subscription refresher (D-M4-I01). Both idle when no account is signed in.

using Barbican.Core.Calendar.Feeds;
using Barbican.Core.Calendar.Reminders;
using Barbican.Presentation.Composition;

namespace Barbican.Presentation.Calendar;

/// <summary>The calendar's background timers.</summary>
public sealed class CalendarBackground(IReminderScheduler reminders, CalendarSubscriptionRefresher refresher) : IStartupTask
{
    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        reminders.Start();
        refresher.Start();
        return Task.CompletedTask;
    }
}
