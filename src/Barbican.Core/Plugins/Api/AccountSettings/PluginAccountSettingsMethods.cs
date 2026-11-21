// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:196-197,217,329,356,2221-2233,2438-2460 and assistant/c.ts:32-33,93-94 (accountData.get email:read, accountData.set email:write, plugin.getConfig no static gate, identities.update identity:write,
// notifications.show ui:notifications, vacation.get vacation:read, vacation.set vacation:write).
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Settings.Account;
using Barbican.Core.Settings.Vacation;

namespace Barbican.Core.Plugins.Api.AccountSettings;

/// <summary>Factory for the vacation.*, identities.update, accountData.*, plugin.getConfig and notifications.show methods.</summary>
public static class PluginAccountSettingsMethods
{
    /// <summary>How many methods <see cref="Create"/> returns.</summary>
    public const int Count = 7;

    // present shows one attributed toast for the add-on (the host's toast presenter); its failure is the call's failure
    public static IReadOnlyList<IPluginApiMethod> Create(IVacationService vacation, IAccountIdentities identities, IPluginPublicConfig config, IPluginMailRateLimiter limiter,
        Func<string, IPluginInvocationContext, CancellationToken, Task<PluginErrorCode?>> present)
    {
        ArgumentNullException.ThrowIfNull(vacation);
        ArgumentNullException.ThrowIfNull(identities);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(limiter);
        ArgumentNullException.ThrowIfNull(present);
        return
        [
            new PluginVacationGet(vacation), new PluginVacationSet(vacation, limiter), new PluginIdentitiesUpdate(identities, limiter),
            new PluginAccountDataGet(limiter), new PluginAccountDataSet(limiter), new PluginGetConfig(config), new PluginNotificationsShow(limiter, present),
        ];
    }
}
