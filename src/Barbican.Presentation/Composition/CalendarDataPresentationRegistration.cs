// Mirrors: M4 plan section 4.6 (DI). Owner cal-data: the reminder popup view model.

using Barbican.Presentation.Calendar.Reminders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Barbican.Presentation.Composition;

/// <summary>The portable services of M4 stream cal-data.</summary>
public static class CalendarDataPresentationRegistration
{
    public static IServiceCollection AddBarbicanCalendarDataViewModels(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IReminderPopup, ReminderPopup>();
        return services;
    }
}
