// Mirrors: M6 plan section 4.9 (DI). Owner set-ui: the settings screen (registered as the IAppScreenFactory of the rail's Settings app), the generic rows, the account, security, assistant and backup pages, the assistant column.
// A sample-data run (BARBICAN_SAMPLE=1, Automation build) swaps the data services for the Sample* fakes of Barbican.Presentation.Settings.Sample (D-M6-00); the settings values stay real (SettingValues over the run's own profile) so theme and style apply live.

using Barbican.App.Plugins;
using Barbican.App.Settings;
using Barbican.App.Shell;
using Barbican.App.Views.Settings;
using Barbican.App.Views.Dialogs;
using Barbican.Presentation.Composition;
using Microsoft.Extensions.DependencyInjection;
#if AUTOMATION
using Microsoft.Extensions.DependencyInjection.Extensions;
using Barbican.App.Views.Settings.Automation;
using Barbican.Automation;
using Barbican.Core.Settings.Account;
using Barbican.Core.Settings.Assistant;
using Barbican.Core.Settings.Backup;
using Barbican.Core.Settings.Pages;
using Barbican.Presentation.Settings.Sample;
using Barbican.Presentation.Settings.Ui;
#endif

namespace Barbican.App.Composition.Features;

/// <summary>The settings screen (M6 stream set-ui).</summary>
public static class SettingsUiRegistration
{
    public static IServiceCollection AddBarbicanSettingsUi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _ = services.AddBarbicanSettingsUiViewModels();
        services.AddSingleton<IAppScreenFactory, SetUiScreenFactory>();
        services.AddSingleton<PluginPackageHost>();
        services.AddSingleton<ISettingsPageFactory, AddonsPageFactory>();
        services.AddSingleton<IDialogContent, AccessibilityQuickContent>();
        services.AddSingleton<IDialogContent, TimeZonePickerContent>();
#if AUTOMATION
        if (string.Equals(Environment.GetEnvironmentVariable("BARBICAN_SAMPLE"), "1", StringComparison.Ordinal))
        {
            services.Replace(ServiceDescriptor.Singleton<ISettingInfo, SetUiSampleInfo>());
            services.Replace(ServiceDescriptor.Singleton<ISettingBackup, SampleSettingBackup>());
            services.Replace(ServiceDescriptor.Singleton<IAccountSecurity, SampleAccountSecurity>());
            services.Replace(ServiceDescriptor.Singleton<IAccountQuota, SampleAccountQuota>());
            services.Replace(ServiceDescriptor.Singleton<IAccountIdentities, SampleAccountIdentities>());
            services.Replace(ServiceDescriptor.Singleton<IAssistantSession, SampleAssistantSession>());
        }

        for (var i = 0; i < SetUiCommand.Count; i++)
        {
            var index = i;
            services.AddSingleton<IAutomationCommand>(sp => SetUiCommand.Create(sp).ElementAt(index));
        }
#endif
        return services;
    }
}
