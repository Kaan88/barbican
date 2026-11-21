// Composition helper: one set of vacation.*, identities.update, accountData.*, plugin.getConfig and notifications.show adapters per host, registered method by method in the app's container.
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Settings.Account;
using Barbican.Core.Settings.Vacation;

namespace Barbican.Core.Plugins.Api.AccountSettings;

/// <summary>The settings adapters of one host.</summary>
public sealed class PluginAccountSettingsMethodSet(IVacationService vacation, IAccountIdentities identities, IPluginPublicConfig config, IPluginMailRateLimiter limiter,
    Func<string, IPluginInvocationContext, CancellationToken, Task<PluginErrorCode?>> present)
{
    public IReadOnlyList<IPluginApiMethod> Methods { get; } = PluginAccountSettingsMethods.Create(vacation, identities, config, limiter, present);
}
