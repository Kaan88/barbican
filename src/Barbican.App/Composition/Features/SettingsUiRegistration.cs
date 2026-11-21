// Mirrors: M6 plan section 4.9 (DI). Owner set-ui: the settings screen (registered as the IAppScreenFactory of the rail's Settings app), the generic rows, the account, security, add-on (assistant) and backup pages, the add-on chat column.
// A sample-data run (BARBICAN_SAMPLE=1, Automation build) swaps the data services for the Sample* fakes of Barbican.Presentation.Settings.Sample (D-M6-00); the settings values stay real (SettingValues over the run's own profile) so theme and style apply live.

using Barbican.App.Plugins;
using Barbican.App.Settings;
using Barbican.App.Shell;
using Barbican.App.Views.Settings;
using Barbican.App.Views.Dialogs;
using Barbican.Core.Plugins.Bridge;
#if WINDOWS
using Barbican.Core.Accounts;
using Barbican.Core.Plugins.Api.Dialogs;
using Barbican.Core.Plugins.Notifications;
using Barbican.Core.Plugins.Packages.Managed;
#if AUTOMATION
using Barbican.Core.Storage;
#endif
#endif
using Barbican.Presentation.Composition;
using Microsoft.Extensions.DependencyInjection;
#if AUTOMATION
using Microsoft.Extensions.DependencyInjection.Extensions;
using Barbican.App.Views.Settings.Automation;
using Barbican.Automation;
using Barbican.Core.Settings.Account;
using Barbican.Presentation.Settings.Addons;
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
#if WINDOWS
        services.AddSingleton(CreateManagedPluginSource);
        services.AddSingleton<IAccountSignOutCleanup>(sp => new PluginAccountSignOutCleanup(() => sp.GetRequiredService<PluginPackageHost>()));
        services.AddSingleton<Views.Plugins.PluginFilePresenter>();
        services.AddSingleton<Barbican.Core.OrganizationPolicy.OrganizationPrintPolicy>();
        services.AddSingleton<Views.Plugins.PluginPrintPresenter>();
        services.AddSingleton<IPluginApiMethod>(sp => new PluginPrint(sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<Views.Plugins.PluginPrintPresenter>().ShowAsync));
        services.AddSingleton<Views.Plugins.PluginProgressToasts>();
        services.AddSingleton<Views.Plugins.PluginToastPresenter>();
        for (var toastMethod = 0; toastMethod < PluginToastMethods.FullMethodCount; toastMethod++)
        {
            var method = toastMethod;
            services.AddSingleton<IPluginApiMethod>(sp => sp.GetRequiredService<Views.Plugins.PluginToastPresenter>().Methods[method]);
        }
        services.AddSingleton<IDialogContent, Views.Plugins.PluginFilePreviewContent>();
        services.AddSingleton<IDialogContent, Views.Plugins.PluginDialogContent>();
        services.AddSingleton<Views.Plugins.PluginDialogPresenter>();
        for (var dialogMethod = 0; dialogMethod < PluginDialogMethods.MethodCount; dialogMethod++)
        {
            var method = dialogMethod;
            services.AddSingleton<IPluginApiMethod>(sp => sp.GetRequiredService<Views.Plugins.PluginDialogPresenter>().Methods[method]);
        }
        services.AddSingleton<IPluginApiMethod>(sp => new PluginOpenFile(sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<Views.Plugins.PluginFilePresenter>().ShowAsync));
#endif
        services.AddSingleton<PluginBackgroundRuntimes>();
        services.AddSingleton<IPluginSessionSource, PluginSessionSource>();
        services.AddSingleton<ISettingsPageFactory, AddonsPageFactory>();
        services.AddSingleton<IDialogContent, AccessibilityQuickContent>();
        services.AddSingleton<IDialogContent, TimeZonePickerContent>();
        services.AddSingleton<IDialogContent, TrustedSendersDialogContent>();
#if AUTOMATION
        if (string.Equals(Environment.GetEnvironmentVariable("BARBICAN_SAMPLE"), "1", StringComparison.Ordinal))
        {
            services.Replace(ServiceDescriptor.Singleton<ISettingInfo, SetUiSampleInfo>());
            services.Replace(ServiceDescriptor.Singleton<ISettingBackup, SampleSettingBackup>());
            services.Replace(ServiceDescriptor.Singleton<IAccountSecurity, SampleAccountSecurity>());
            services.Replace(ServiceDescriptor.Singleton<IAccountQuota, SampleAccountQuota>());
            services.Replace(ServiceDescriptor.Singleton<IAccountIdentities, SampleAccountIdentities>());
            services.Replace(ServiceDescriptor.Singleton<IAddonPageSource, SampleAddonPageSource>());
        }

        services.AddSingleton<IAutomationCommand, AddonsFixtureCommand>();
#if WINDOWS
        services.AddSingleton<IAutomationCommand, AddonsPrintStateCommand>();
#endif
        for (var i = 0; i < SetUiCommand.Count; i++)
        {
            var index = i;
            services.AddSingleton<IAutomationCommand>(sp => SetUiCommand.Create(sp).ElementAt(index));
        }
#endif
        return services;
    }

#if WINDOWS
    private static ManagedPluginSource CreateManagedPluginSource(IServiceProvider services)
    {
        var snapshots = services.GetRequiredService<IAccountSessionSnapshots>();
#if AUTOMATION
        if (Shell.Automation.SamplePolicyFixture.Enabled(services.GetRequiredService<IProfilePaths>()) &&
            string.Equals(Environment.GetEnvironmentVariable("BARBICAN_SAMPLE_SEND_PIPELINE"), "1", StringComparison.Ordinal) &&
            string.Equals(Environment.GetEnvironmentVariable("BARBICAN_SAMPLE_MANAGED_ADDONS"), "1", StringComparison.Ordinal))
            return Shell.Automation.SampleManagedPluginFixture.Create(snapshots);
#endif
        return new ManagedPluginSource(snapshots);
    }
#endif
}
