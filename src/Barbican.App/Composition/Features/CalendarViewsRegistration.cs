// Mirrors: M4 plan section 4.6 (DI). Owner cal-views: the calendar screen (Day, Week, Month, Agenda, sidebar with the mini month and the calendar list, toolbar, print), registered as the Calendar app of the rail.
// A sample-data run (BARBICAN_SAMPLE=1, Automation build) swaps the store for an in-memory source and writer, and adds the cal-* commands.

using Barbican.Presentation.Composition;
using Microsoft.Extensions.DependencyInjection;
#if AUTOMATION
using Barbican.Automation;
using Barbican.App.Views.Calendar.Automation;
using Barbican.Core.Calendar.Editing;
using Barbican.Presentation.Calendar.Grid;
using Microsoft.Extensions.DependencyInjection.Extensions;
#endif

namespace Barbican.App.Composition.Features;

/// <summary>The calendar screen (M4 stream cal-views).</summary>
public static class CalendarViewsRegistration
{
    public static IServiceCollection AddBarbicanCalendarViews(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _ = services.AddBarbicanCalendarViewsViewModels();
#if AUTOMATION
        if (string.Equals(Environment.GetEnvironmentVariable("BARBICAN_SAMPLE"), "1", StringComparison.Ordinal))
        {
            services.TryAddSingleton<SampleCalendarSource>();
            services.Replace(ServiceDescriptor.Singleton<ICalendarSource>(sp => sp.GetRequiredService<SampleCalendarSource>()));
            services.Replace(ServiceDescriptor.Singleton<ICalendarWriter>(sp => sp.GetRequiredService<SampleCalendarSource>()));
            services.Replace(ServiceDescriptor.Singleton<Barbican.Presentation.Calendar.Editors.IEditorSource>(sp => new SampleEditorSource(sp.GetRequiredService<SampleCalendarSource>())));
        }

        for (var i = 0; i < 8; i++)
        {
            var index = i;
            services.AddSingleton<IAutomationCommand>(sp => CalendarCommand.Create(sp).ElementAt(index));
        }
#endif
        return services;
    }
}
