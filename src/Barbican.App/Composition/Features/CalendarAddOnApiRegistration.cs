#if WINDOWS
// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:308-321 and assistant/c.ts (calendar.* and tasks.* methods). Registers the scoped calendar and task add-on methods over the calendar store and writer the calendar views use.
using Barbican.Core.Calendar;
using Barbican.Core.Calendar.Editing;
using Barbican.Core.Calendar.Feeds;
using Barbican.Core.Calendar.Invitations;
using Barbican.Core.Plugins.Api.Calendar;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Barbican.App.Composition.Features;

internal static class CalendarAddOnApiRegistration
{
    /// <summary>The 15 calendar and task methods; each needs its own grant (calendar:read / write / respond, tasks:read / write) for the login it runs on.</summary>
    internal static IServiceCollection AddCalendarAddOnApi(this IServiceCollection services)
    {
        services.AddSingleton<IPluginCalendarHost>(sp => new PluginCalendarHost(sp.GetRequiredService<ICalendarStore>(), sp.GetRequiredService<ICalendarWriter>(),
            sp.GetRequiredService<IInvitationService>(), sp.GetService<ICalendarSubscriptions>(), sp.GetRequiredService<IPrefsStore>(), sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<PluginCalendarMethodSet>();
        for (var index = 0; index < PluginCalendarMethods.Count; index++)
        {
            var method = index;
            services.AddSingleton<IPluginApiMethod>(sp => sp.GetRequiredService<PluginCalendarMethodSet>().Methods[method]);
        }

        return services;
    }
}
#endif
