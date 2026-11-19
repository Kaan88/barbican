// Mirrors: M4 plan section 4.6 (DI). Owner cal-data: the store, the reminder scheduler and its state. Each default is registered with TryAddSingleton; cal-data replaces the class and keeps this file's lines.

using Barbican.Core.Calendar.Reminders;
using Barbican.Core.Sync;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Barbican.Core.Calendar;

/// <summary>Registers the calendar data layer (M4 stream cal-data).</summary>
public static class CalendarDataRegistration
{
    public static IServiceCollection AddBarbicanCalendarData(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<ICalendarPush, CalendarPushRelay>();
        services.TryAddSingleton<ICalendarStore, CalendarStore>();
        services.TryAddSingleton<ICalendarParticipantIdentities, CalendarParticipantIdentities>();
        services.TryAddSingleton<IReminderStateStore, ReminderStateStore>();
        services.TryAddSingleton<IReminderScheduler, ReminderScheduler>();
        return services;
    }
}
