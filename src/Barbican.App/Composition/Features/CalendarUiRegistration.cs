// Mirrors: M4 plan section 4.6 (DI). Owner cal-ui: the event and task editors, the tasks view, the scope dialog, the reminder popup views, the calendar commands, menus and shortcuts.

using Barbican.App.Views.Calendar.Editors;
using Barbican.Presentation.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace Barbican.App.Composition.Features;

/// <summary>The calendar editors, tasks and commands (M4 stream cal-ui).</summary>
public static class CalendarUiRegistration
{
    public static IServiceCollection AddBarbicanCalendarUi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _ = services.AddBarbicanCalendarUiViewModels();
        // cal-views' calendar screen host places this one view over the grid (one child); it draws the editor cards and the questions
        services.AddSingleton<CalendarCardHost>();
#if AUTOMATION
        _ = Views.Calendar.Editors.Automation.CalendarUiAutomation.AddBarbicanCalendarUiAutomation(services);
#endif
        return services;
    }
}
