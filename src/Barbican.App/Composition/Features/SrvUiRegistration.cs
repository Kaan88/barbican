// Mirrors: M6 plan section 4.9 (DI). Owner srv-ui: one ISettingsPageFactory per server-backed page (identities, templates, organize, filters, vacation, recover), the rule editor, apply and Sieve dialogs, the folder and recover sources.
// A sample-data run (BARBICAN_SAMPLE=1, Automation build) swaps the data services for the Sample* fakes of Barbican.Presentation.Settings.Sample (D-M6-00) and adds the srv-* automation commands.

using Barbican.App.Settings;
using Barbican.App.Views.Dialogs;
using Barbican.App.Views.SettingsServer;
using Barbican.Core.Accounts;
using Barbican.Presentation.Composition;
using Barbican.Presentation.Settings;
using Barbican.Presentation.Settings.Server;
using Barbican.Presentation.Shell;
using Barbican.Presentation.Tags;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
#if AUTOMATION
using Barbican.App.Views.SettingsServer.Automation;
using Barbican.Automation;
using Barbican.Core.Filters.Rules;
using Barbican.Core.Settings.Account;
using Barbican.Core.Settings.Folders;
using Barbican.Core.Settings.Templates;
using Barbican.Core.Settings.Vacation;
using Barbican.Presentation.Settings.Sample;
#endif

namespace Barbican.App.Composition.Features;

/// <summary>The server-backed settings pages (M6 stream srv-ui).</summary>
public static class SrvUiRegistration
{
    public static IServiceCollection AddBarbicanServerUi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _ = services.AddBarbicanServerUiViewModels();
        var sample = false;
#if AUTOMATION
        sample = string.Equals(Environment.GetEnvironmentVariable("BARBICAN_SAMPLE"), "1", StringComparison.Ordinal);
        if (sample)
        {
            services.Replace(ServiceDescriptor.Singleton<IFilterService, SampleFilterService>());
            services.Replace(ServiceDescriptor.Singleton<IVacationService, SampleVacationService>());
            services.Replace(ServiceDescriptor.Singleton<IFolderAdmin, SampleFolderAdmin>());
            services.Replace(ServiceDescriptor.Singleton<ITemplateAdmin, SampleTemplateAdmin>());
            services.Replace(ServiceDescriptor.Singleton<IAccountIdentities, SampleAccountIdentities>());
        }
#endif
        if (sample)
        {
            services.Replace(ServiceDescriptor.Singleton<ISrvUiFolderSource, SrvUiSampleFolders>());
            services.Replace(ServiceDescriptor.Singleton<ISrvUiRecoverSource, SrvUiSampleRecover>());
        }
        else
        {
            services.TryAddSingleton<ISrvUiFolderSource>(sp => new SrvUiSidebarFolders(sp.GetRequiredService<SidebarViewModel>()));
            services.TryAddSingleton<ISrvUiRecoverSource>(sp => new SrvUiArchivedSource(sp.GetRequiredService<IAccountManager>()));
        }

        services.TryAddSingleton<SrvUiFoldersViewModel>();
        services.TryAddSingleton<SrvUiRecoverViewModel>();
        services.TryAddSingleton(sp => new SrvUiTagsViewModel(sp.GetRequiredService<TagService>()));
        services.AddSingleton<IDialogContent, SrvUiFilterEditorDialogContent>();
        services.AddSingleton<IDialogContent, SrvUiFilterApplyDialogContent>();
        services.AddSingleton<IDialogContent, SrvUiSieveDialogContent>();
        Page(services, SettingsPageIds.Filters, sp => new SrvUiFiltersPage(sp.GetRequiredService<ISrvUiFiltersViewModel>()));
        Page(services, SettingsPageIds.Vacation, sp => new SrvUiVacationPage(sp.GetRequiredService<SrvUiVacationViewModel>()));
        Page(services, SettingsPageIds.Identities, sp => new SrvUiIdentitiesPage(sp.GetRequiredService<SrvUiIdentitiesViewModel>()));
        Page(services, SettingsPageIds.Templates, sp => new SrvUiTemplatesPage(sp.GetRequiredService<SrvUiTemplatesViewModel>()));
        Page(services, SettingsPageIds.Organize, sp => new SrvUiOrganizePage(sp.GetRequiredService<SrvUiFoldersViewModel>(), sp.GetRequiredService<SrvUiTagsViewModel>()));
        Page(services, SettingsPageIds.Recover, sp => new SrvUiRecoverPage(sp.GetRequiredService<SrvUiRecoverViewModel>()));
#if AUTOMATION
        for (var i = 0; i < SrvUiCommand.Count; i++)
        {
            var index = i;
            services.AddSingleton<IAutomationCommand>(sp => SrvUiCommand.Create(sp).ElementAt(index));
        }
#endif
        return services;
    }

    private static void Page(IServiceCollection services, string id, Func<IServiceProvider, SrvUiPage> create) =>
        services.AddSingleton<ISettingsPageFactory>(sp => new SrvUiPageFactory(id, () => create(sp)));
}
