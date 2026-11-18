// Mirrors: M4 plan section 4.6 (DI). Owner cal-ui: the editors, the tasks view model, the calendar commands and menus.

using Barbican.Core.Accounts;
using Barbican.Core.Calendar;
using Barbican.Core.Calendar.Editing;
using Barbican.Core.Calendar.Feeds;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Storage;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Calendar.Editors;
using Barbican.Presentation.Calendar.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Barbican.Presentation.Composition;

/// <summary>The portable services of M4 stream cal-ui.</summary>
public static class CalendarUiPresentationRegistration
{
    public static IServiceCollection AddBarbicanCalendarUiViewModels(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<ICalendarRules, CalendarRules>();
        services.TryAddSingleton<ICalendarSource>(sp => new CalendarSource(sp.GetRequiredService<ICalendarStore>(), sp.GetService<IAccountManager>(), sp.GetService<ICalendarSubscriptions>()));
        services.TryAddSingleton<CalendarEditorHost>();
        services.TryAddSingleton<ICalendarQuestions>(sp => sp.GetRequiredService<CalendarEditorHost>());
        services.AddSingleton<CalendarEditors>(sp => new CalendarEditors(
            sp.GetRequiredService<ICalendarSource>(),
            sp.GetRequiredService<ICalendarWriter>(),
            sp.GetRequiredService<ICalendarRules>(),
            sp.GetRequiredService<CalendarEditorHost>(),
            sp.GetService<IAvailabilityService>(),
            sp.GetService<IContactCardReader>(),
            sp.GetService<IHookBus>(),
            sp.GetService<IToastService>(),
            sp.GetService<TimeProvider>(),
            sp.GetService<IPrefsStore>()));
        services.Replace(ServiceDescriptor.Singleton<ICalendarEditors>(sp => sp.GetRequiredService<CalendarEditors>()));
        services.TryAddSingleton(sp => new TasksViewModel(
            sp.GetRequiredService<ICalendarSource>(),
            sp.GetRequiredService<ICalendarWriter>(),
            sp.GetRequiredService<ICalendarEditors>(),
            sp.GetService<IHookBus>(),
            sp.GetService<IToastService>(),
            sp.GetService<TimeProvider>(),
            () => sp.GetService<IPrefsStore>() is { } p && CalendarSettings.Read(p.Current).TwelveHour,
            null,
            sp.GetRequiredService<CalendarEditorHost>()));
        return services;
    }
}
