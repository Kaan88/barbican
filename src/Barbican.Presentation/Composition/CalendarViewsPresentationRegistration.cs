// Mirrors: M4 plan section 4.6 (DI). Owner cal-views: the calendar view model and the gesture rules.

using Barbican.Presentation.Calendar.Grid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Barbican.Presentation.Composition;

/// <summary>The portable services of M4 stream cal-views.</summary>
public static class CalendarViewsPresentationRegistration
{
    public static IServiceCollection AddBarbicanCalendarViewsViewModels(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<ICalendarViewModel, CalendarViewModel>();
        services.TryAddSingleton<ICalendarGestures, CalendarGestures>();
        return services;
    }
}
