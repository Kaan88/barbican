// Mirrors: M6 plan section 4.9 (DI). Owner set-data: the settings values, backup, info, assistant and account-level services. Defaults use TryAddSingleton; the stubs have no constructor parameters until set-data replaces them.

using Barbican.Core.Mail.Write;
using Barbican.Core.Settings.Account;
using Barbican.Core.Settings.Assistant;
using Barbican.Core.Settings.Backup;
using Barbican.Core.Settings.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Barbican.Core.Settings;

/// <summary>Registers the settings data layer (M6 stream set-data).</summary>
public static class SettingsDataRegistration
{
    public static IServiceCollection AddBarbicanSettingsData(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<ISettingValues, SettingValues>();
        services.TryAddSingleton<ISettingBackup, SettingBackup>();
        services.TryAddSingleton<ISettingInfo, SettingInfo>();
        services.TryAddSingleton<IAccountIdentities, AccountIdentities>();
        services.TryAddSingleton<IAccountSecurity, AccountSecurity>();
        services.TryAddSingleton<IAccountQuota, AccountQuota>();
        services.TryAddSingleton<IAssistantKeys, AssistantKeys>();
        services.TryAddSingleton<IAssistantSettings, AssistantSettings>();
        services.TryAddSingleton<IAssistantClient, AssistantClient>();
        services.TryAddSingleton<IMailWriterFactory, MailWriterFactory>();
        services.TryAddSingleton<IAssistantTools, AssistantTools>();
        services.TryAddSingleton<IAssistantSession, AssistantSession>();
        return services;
    }
}
