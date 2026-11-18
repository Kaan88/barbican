// Mirrors: M4 plan section 4.6 (DI). Owner cal-views: the calendar screen (Day, Week, Month, Agenda, sidebar with the mini month and the calendar list, toolbar, print), registered as the Calendar app of the rail.

using Barbican.Presentation.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace Barbican.App.Composition.Features;

/// <summary>The calendar screen (M4 stream cal-views).</summary>
public static class CalendarViewsRegistration
{
    public static IServiceCollection AddBarbicanCalendarViews(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _ = services.AddBarbicanCalendarViewsViewModels();
        return services;
    }
}
