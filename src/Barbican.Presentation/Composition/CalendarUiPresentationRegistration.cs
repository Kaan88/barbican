// Mirrors: M4 plan section 4.6 (DI). Owner cal-ui: the editors, the tasks view model, the calendar commands and menus.

using Barbican.Presentation.Calendar.Editors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Barbican.Presentation.Composition;

/// <summary>The portable services of M4 stream cal-ui.</summary>
public static class CalendarUiPresentationRegistration
{
    public static IServiceCollection AddBarbicanCalendarUiViewModels(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<ICalendarEditors, CalendarEditors>();
        return services;
    }
}
