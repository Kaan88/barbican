#if WINDOWS
// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:233,277-296,2118-2127,2310-2349 (see Core Plugins/Api/Navigation). Registers ui.openEmail, ui.openCalendar, ui.openTask, ui.refreshRowBadges, ui.rerenderEmail,
// ui.rerenderFetchedEmails, ui.downloadFile, ui.setPages and ui.openPage over the shell's own paths (PluginAppNavigator) and the platform save dialog (PluginFileSaver).
// search.run, search.clear and tabs.* are not registered (docs/milestones/M6b-api-inventory.md).
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Api.Navigation;
using Barbican.Core.Plugins.Bridge;
using Barbican.Presentation.Calendar.Grid;
using Barbican.Presentation.Calendar.Tasks;
using Barbican.Presentation.Common;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Mail.Loading;
using Barbican.Presentation.Shell;
using Barbican.Presentation.Unified;
using Microsoft.Extensions.DependencyInjection;

namespace Barbican.App.Composition.Features;

internal static class NavigationAddOnApiRegistration
{
    /// <summary>The 9 navigation methods; each needs its own grant for the login it runs on (ui.refreshRowBadges, ui.rerenderEmail and ui.rerenderFetchedEmails need none, as in Bulwark).</summary>
    internal static IServiceCollection AddNavigationAddOnApi(this IServiceCollection services)
    {
        services.AddSingleton(sp => new Barbican.App.Plugins.PluginAppNavigator(sp.GetRequiredService<ShellViewModel>(), sp.GetRequiredService<IActiveMail>(),
            sp.GetRequiredService<MessageListViewModel>(), sp.GetRequiredService<IMessageOpener>(), sp.GetRequiredService<MessageLoader>(),
            sp.GetRequiredService<CalendarViewModel>(), sp.GetRequiredService<TasksViewModel>(), sp.GetRequiredService<IDispatcher>()));
        services.AddSingleton(sp => new PluginNavigationHost(sp.GetRequiredService<IPluginMailReaders>(), sp.GetRequiredService<IPluginMailRateLimiter>(),
            sp.GetRequiredService<Barbican.App.Plugins.PluginAppNavigator>(), sp.GetRequiredService<Barbican.App.Plugins.PluginAppNavigator>(),
            new Barbican.App.Plugins.PluginFileSaver(sp.GetRequiredService<TransferPicks>(), sp.GetRequiredService<IDispatcher>()),
            sp.GetRequiredService<Barbican.Core.Plugins.Pages.PluginPageRegistry>(), sp.GetRequiredService<Barbican.App.Plugins.PluginPageOpener>()));
        services.AddSingleton(sp => new Barbican.App.Plugins.PluginPageOpener(sp.GetRequiredService<ShellViewModel>(), sp.GetRequiredService<Barbican.Core.Plugins.Pages.PluginPageRegistry>(), sp.GetRequiredService<IDispatcher>()));
        services.AddSingleton<PluginNavigationMethodSet>();
        for (var index = 0; index < PluginNavigationMethods.Count; index++)
        {
            var method = index;
            services.AddSingleton<IPluginApiMethod>(sp => sp.GetRequiredService<PluginNavigationMethodSet>().Methods[method]);
        }

        return services;
    }
}
#endif
