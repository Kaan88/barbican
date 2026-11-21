// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts permission table for user.*, app.getInfo, keywords.* and templates.* (see Core Plugins/Api/Account, App, MailTags, Templates).
// Registers those host methods with the plugin dispatcher. They reuse the mail-read rate limiter and reader factory registered by the composer feature, the user's
// preferences (the tag definitions the sidebar reads) and the templates settings service. Windows head only, like the other add-on host methods.
#if WINDOWS
using Barbican.Core.Accounts;
using Barbican.Core.Plugins.Api.Account;
using Barbican.Core.Plugins.Api.App;
using Barbican.Core.Plugins.Api.MailTags;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Api.Templates;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Settings.Templates;
using Barbican.Core.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Barbican.App.Composition.Features;

/// <summary>The account, app, tag and template host methods for add-ons.</summary>
public static class PluginAccountApiRegistration
{
    public static IServiceCollection AddBarbicanPluginAccountApi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IPluginAppInfo, PluginAppInfo>();
        services.AddSingleton(sp => new PluginTagDefinitions(sp.GetRequiredService<IPrefsStore>(), sp.GetRequiredService<IPrefsEditor>()));
        services.AddSingleton<PluginKeywordCounts>();
        for (var index = 0; index < 5; index++)
        {
            var method = index;
            services.AddSingleton<IPluginApiMethod>(sp => PluginAccountMethods.Create(sp.GetRequiredService<IAccountManager>(), sp.GetRequiredService<IPrefsStore>(), sp.GetRequiredService<TimeProvider>())[method]);
        }

        services.AddSingleton<IPluginApiMethod>(sp => new PluginAppGetInfo(sp.GetRequiredService<IPluginAppInfo>()));
        for (var index = 0; index < 8; index++)
        {
            var method = index;
            services.AddSingleton<IPluginApiMethod>(sp => PluginKeywordMethods.Create(sp.GetRequiredService<PluginTagDefinitions>(), sp.GetRequiredService<PluginKeywordCounts>(),
                sp.GetRequiredService<IPluginMailReaders>(), sp.GetRequiredService<IPluginMailRateLimiter>())[method]);
        }

        for (var index = 0; index < 5; index++)
        {
            var method = index;
            services.AddSingleton<IPluginApiMethod>(sp => PluginTemplateMethods.Create(sp.GetRequiredService<ITemplateAdmin>(), sp.GetRequiredService<IPluginMailRateLimiter>())[method]);
        }

        return services;
    }
}
#endif
