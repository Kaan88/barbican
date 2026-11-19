// Mirrors: M6 plan section 4.9 (DI). Owner set-ui: the settings screen's view models, the settings commands and the palette's settings pages.

using Barbican.Core.Accounts;
using Barbican.Core.Settings.Account;
using Barbican.Core.Settings.Assistant;
using Barbican.Core.Settings.Backup;
using Barbican.Core.Settings.Pages;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Common;
using Barbican.Presentation.Dialogs;
using Barbican.Presentation.Palette;
using Barbican.Presentation.Settings;
using Barbican.Presentation.Settings.Ui;
using Barbican.Presentation.Shell;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Barbican.Presentation.Composition;

/// <summary>The portable services of M6 stream set-ui.</summary>
public static class SettingsUiPresentationRegistration
{
    public static IServiceCollection AddBarbicanSettingsUiViewModels(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<ISetUiViewModel>(sp => new SetUiViewModel(
            sp.GetRequiredService<ISettingValues>(),
            sp.GetRequiredService<ISettingInfo>(),
            sp.GetRequiredService<ISettingsNavigation>(),
            sp.GetService<IAccountManager>(),
            sp.GetService<IDispatcher>()));
        services.TryAddSingleton(sp => new SetUiSecurityViewModel(
            sp.GetRequiredService<IAccountSecurity>(),
            sp.GetRequiredService<IAccountQuota>(),
            sp.GetRequiredService<ISetUiViewModel>(),
            sp.GetService<IDispatcher>()));
        services.TryAddSingleton(sp => new SetUiAssistantViewModel(sp.GetRequiredService<IAssistantSession>(), sp.GetRequiredService<ISetUiViewModel>(), sp.GetService<IDispatcher>()));
        services.TryAddSingleton(sp => new SetUiBackupViewModel(sp.GetRequiredService<ISettingBackup>(), sp.GetService<IDialogService>()));
        services.TryAddSingleton<AccessibilityQuickModel>();
        services.TryAddSingleton(sp => new SetUiCommandSet(SetUiCommandFactory.Create(sp.GetRequiredService<ShellViewModel>(), sp.GetRequiredService<ISettingsNavigation>(), sp.GetRequiredService<ISetUiViewModel>(), () => sp.GetRequiredService<IDialogService>(), sp.GetRequiredService<AccessibilityQuickModel>())));
        for (var i = 0; i < SetUiCommands.Count; i++)
        {
            var index = i;
            services.AddSingleton<IAppCommand>(sp => sp.GetRequiredService<SetUiCommandSet>().Commands[index]);
        }

        DecoratePalette(services);
        return services;
    }

    // The palette lists the settings pages when it is opened in Settings: wrap whatever IPaletteContextSource is registered.
    private static void DecoratePalette(IServiceCollection services)
    {
        var inner = services.LastOrDefault(d => d.ServiceType == typeof(IPaletteContextSource));
        if (inner is null)
        {
            return;
        }

        _ = services.Remove(inner);
        services.Add(ServiceDescriptor.Describe(
            typeof(IPaletteContextSource),
            sp =>
            {
                IPaletteContextSource wrapped = inner switch
                {
                    { ImplementationInstance: IPaletteContextSource i } => i,
                    { ImplementationFactory: { } f } => (IPaletteContextSource)f(sp),
                    { ImplementationType: { } t } => (IPaletteContextSource)ActivatorUtilities.CreateInstance(sp, t),
                    _ => throw new InvalidOperationException("The palette context source has no implementation."),
                };
                return new SetUiPaletteContextSource(wrapped, sp.GetRequiredService<ISettingsNavigation>(), sp.GetRequiredService<ISetUiViewModel>());
            },
            inner.Lifetime));
    }
}
