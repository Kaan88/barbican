#if WINDOWS
// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:235-237,334-354, host-api-mail.ts, assistant/b.ts and c.batch.ts, host-api-compose.ts (see Core Plugins/Api/MailActions). Registers email.move, archive, setKeyword, removeKeyword,
// updateKeywords, batch, undoBatch, getAttachment, getAttachmentText, getRaw, drafts.create, compose.open and compose.reply over the writer the list's actions use, the hook bus, the draft service and the composer service.
// email.forward is not registered: it sends mail, and Barbican has no forward pipeline for add-ons yet (docs/FABLE-REVIEW.md, "Mail-action add-on methods").
using Barbican.Core.Compose;
using Barbican.Core.Mail.Write;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Api.MailActions;
using Barbican.Core.Plugins.Api.MailTags;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Storage;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Common;
using Barbican.Presentation.Compose;
using Barbican.Presentation.Mail;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Barbican.App.Composition.Features;

internal static class MailActionsAddOnApiRegistration
{
    /// <summary>The 13 mail-action methods; each needs its own grant (email:write, email:blob-read or compose:open) for the login it runs on.</summary>
    internal static IServiceCollection AddMailActionsAddOnApi(this IServiceCollection services)
    {
        services.TryAddSingleton(sp => new PluginTagDefinitions(sp.GetRequiredService<IPrefsStore>(), sp.GetRequiredService<IPrefsEditor>()));
        services.AddSingleton<IPluginMailUndoHistory>(sp => new Barbican.App.Plugins.PluginMailUndoHistory(sp.GetRequiredService<IUndoService>(),
            sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IPluginComposeLauncher>(sp => new Barbican.App.Plugins.PluginComposeLauncher(sp.GetRequiredService<IComposerService>(),
            sp.GetRequiredService<ReaderViewModel>(), sp.GetRequiredService<IDispatcher>()));
        services.AddSingleton(sp => new PluginMailActionHost(sp.GetRequiredService<IPluginMailReaders>(), sp.GetRequiredService<IMailWriterFactory>(),
            sp.GetRequiredService<IPluginMailRateLimiter>(), sp.GetRequiredService<PluginTagDefinitions>(), sp.GetRequiredService<TimeProvider>(),
            sp.GetService<IHookBus>(), sp.GetRequiredService<IPluginMailUndoHistory>(), sp.GetService<IDraftService>(), sp.GetRequiredService<IPluginComposeLauncher>()));
        services.AddSingleton<PluginMailActionMethodSet>();
        for (var index = 0; index < PluginMailActionMethods.Count; index++)
        {
            var method = index;
            services.AddSingleton<IPluginApiMethod>(sp => sp.GetRequiredService<PluginMailActionMethodSet>().Methods[method]);
        }

        return services;
    }
}
#endif
