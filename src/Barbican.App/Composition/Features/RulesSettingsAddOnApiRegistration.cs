#if WINDOWS
// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts permission table and assistant/c.ts for filters.*, sieve.*, vacation.*, identities.update, accountData.*, plugin.getConfig and notifications.show (see Core Plugins/Api/MailRules and
// Plugins/Api/AccountSettings). Registers those host methods with the plugin dispatcher over the services the Settings pages use (IFilterService, IVacationService, IAccountIdentities), the user's tag definitions and the add-on toast presenter.
// They reuse the rate limiter registered by the composer feature. Windows head only, like the other add-on host methods.
using Barbican.Core.Filters.Rules;
using Barbican.Core.Plugins.Api.AccountSettings;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Api.MailRules;
using Barbican.Core.Plugins.Api.MailTags;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Settings.Account;
using Barbican.Core.Settings.Vacation;
using Barbican.Core.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Barbican.App.Composition.Features;

internal static class RulesSettingsAddOnApiRegistration
{
    /// <summary>The 14 methods: filters.list/create/undo, sieve.isSupported/getActiveScript/validateScript/regenerate (filters:read or filters:write), vacation.get/set, identities.update, accountData.get/set, plugin.getConfig, notifications.show.</summary>
    internal static IServiceCollection AddRulesSettingsAddOnApi(this IServiceCollection services)
    {
        services.TryAddSingleton(sp => new PluginTagDefinitions(sp.GetRequiredService<IPrefsStore>(), sp.GetRequiredService<IPrefsEditor>()));
        services.TryAddSingleton<IPluginPublicConfig, PluginPublicConfigNone>();
        services.AddSingleton(sp => new PluginRulesMethodSet(sp.GetRequiredService<IFilterService>(), sp.GetRequiredService<PluginTagDefinitions>(),
            sp.GetRequiredService<IPluginMailRateLimiter>(), sp.GetRequiredService<TimeProvider>()));
        for (var index = 0; index < PluginRulesMethods.Count; index++)
        {
            var method = index;
            services.AddSingleton<IPluginApiMethod>(sp => sp.GetRequiredService<PluginRulesMethodSet>().Methods[method]);
        }

        services.AddSingleton(sp => new PluginAccountSettingsMethodSet(sp.GetRequiredService<IVacationService>(), sp.GetRequiredService<IAccountIdentities>(),
            sp.GetRequiredService<IPluginPublicConfig>(), sp.GetRequiredService<IPluginMailRateLimiter>(), ToastPresenter(sp.GetRequiredService<Views.Plugins.PluginToastPresenter>())));
        for (var index = 0; index < PluginAccountSettingsMethods.Count; index++)
        {
            var method = index;
            services.AddSingleton<IPluginApiMethod>(sp => sp.GetRequiredService<PluginAccountSettingsMethodSet>().Methods[method]);
        }

        return services;
    }

    // notifications.show shows its text as a toast through the same presenter as toast.info, so the toast is attributed to the add-on, its burst budget applies and nothing is shown for a stale or disabled add-on
    private static Func<string, IPluginInvocationContext, CancellationToken, Task<PluginErrorCode?>> ToastPresenter(Views.Plugins.PluginToastPresenter presenter)
    {
        var info = presenter.Methods.First(m => string.Equals(m.Descriptor.Name, "toast.info", StringComparison.Ordinal));
        return async (message, context, ct) =>
        {
            var builder = new PluginValueBuilder();
            var outcome = await info.InvokeAsync(context, builder.Build(builder.Array(builder.String(message))), ct).ConfigureAwait(false);
            return outcome.IsSuccess ? null : outcome.Error;
        };
    }
}
#endif
