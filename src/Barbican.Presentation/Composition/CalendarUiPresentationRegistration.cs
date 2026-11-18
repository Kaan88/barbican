// Mirrors: M4 plan section 4.6 (DI). Owner cal-ui: the editors, the tasks view model, the calendar commands and menus.

using Barbican.Core.Accounts;
using Barbican.Core.Calendar;
using Barbican.Core.Calendar.Editing;
using Barbican.Core.Calendar.Feeds;
using Barbican.Core.Calendar.Invitations;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Storage;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Calendar.Commands;
using Barbican.Presentation.Calendar.Editors;
using Barbican.Presentation.Calendar.Editors.Dialogs;
using Barbican.Presentation.Calendar.Grid;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Common;
using Barbican.Presentation.Dialogs;
using Barbican.Presentation.Menus;
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
        services.TryAddSingleton<IEditorSource>(sp => new CalendarSource(sp.GetRequiredService<ICalendarStore>(), sp.GetService<IAccountManager>(), sp.GetService<ICalendarSubscriptions>()));
        services.TryAddSingleton<CalendarEditorHost>();
        services.TryAddSingleton<ICalendarQuestions>(sp => sp.GetRequiredService<CalendarEditorHost>());
        services.AddSingleton<CalendarEditors>(sp => new CalendarEditors(
            sp.GetRequiredService<IEditorSource>(),
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
            sp.GetRequiredService<IEditorSource>(),
            sp.GetRequiredService<ICalendarWriter>(),
            sp.GetRequiredService<ICalendarEditors>(),
            sp.GetService<IHookBus>(),
            sp.GetService<IToastService>(),
            sp.GetService<TimeProvider>(),
            () => sp.GetService<IPrefsStore>() is { } p && CalendarSettings.Read(p.Current).TwelveHour,
            null,
            sp.GetRequiredService<CalendarEditorHost>()));
        return services.AddCalendarCommandServices();
    }

    private static IServiceCollection AddCalendarCommandServices(this IServiceCollection services)
    {
        services.TryAddSingleton<ICalendarChrome, NoCalendarChrome>();
        services.TryAddSingleton<ICalendarFilePicker>(_ => new CalendarFilePicker());
        services.TryAddSingleton<ICalendarKeyContext>(sp => sp.GetRequiredService<CalendarEditorHost>());
        services.TryAddSingleton(sp => new CalendarCommandSet(CalendarCommands.Create(new CalendarCommandDeps(
            sp.GetRequiredService<ICalendarViewModel>(),
            sp.GetRequiredService<CalendarEditors>(),
            sp.GetRequiredService<CalendarEditorHost>(),
            sp.GetRequiredService<IEditorSource>(),
            sp.GetRequiredService<ICalendarWriter>(),
            sp.GetRequiredService<ICalendarAdmin>(),
            sp.GetRequiredService<ICalendarSubscriptions>(),
            sp.GetRequiredService<IIcsImporter>(),
            sp.GetRequiredService<IInvitationService>(),
            sp.GetRequiredService<ICalendarRules>(),
            sp.GetRequiredService<ICalendarFilePicker>(),
            sp.GetRequiredService<ICalendarChrome>(),
            sp.GetRequiredService<TasksViewModel>(),
            sp.GetService<TimeProvider>() ?? TimeProvider.System,
            sp.GetService<IClipboardService>(),
            sp.GetService<IToastService>(),
            sp.GetService<IDialogService>()))));
        for (var i = 0; i < CalendarCommandSet.Count; i++)
        {
            var index = i;
            services.AddSingleton<IAppCommand>(sp => sp.GetRequiredService<CalendarCommandSet>().Commands[index]);
        }

        for (var i = 0; i < CalendarMenus.Count; i++)
        {
            var index = i;
            services.AddSingleton<IMenuProvider>(sp => CalendarMenus.Create(sp.GetRequiredService<IEditorSource>())[index]);
        }

        return services;
    }
}
