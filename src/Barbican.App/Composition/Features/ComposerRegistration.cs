// Mirrors: M3 plan section 4.7. Owner composer-ui: the composer shell views, recipient chips, dialogs, the compose commands and shortcuts, mailto and single-instance start-up; the view models come from M3PresentationRegistration.

using Barbican.App.Views.Compose;
using Barbican.App.Views.Dialogs;
#if WINDOWS
using Barbican.Core.Plugins.Api.Composer;
using Barbican.Core.Plugins.Bridge;
#endif
using Barbican.Core.Mail.Body;
using Barbican.Presentation.Composition;
using Barbican.Presentation.Compose;
using Barbican.Presentation.Compose.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Barbican.App.Composition.Features;

/// <summary>The composer shell (M3 stream composer-ui).</summary>
public static class ComposerRegistration
{
    public static IServiceCollection AddBarbicanComposer(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _ = services.AddBarbicanComposerViewModels();
        _ = services.AddBarbicanSendPipelineViewModels();
        services.AddSingleton<Barbican.Presentation.Compose.Pipeline.IScheduleDialogHost, ScheduleDialogHost>();
        // the editor control of editor-view is the composer body
        ComposerBodyHost.EditorFactory = vm => new Views.Editor.EditorView(vm);
        services.AddSingleton<IDialogContent, AccountPickDialogContent>();
        services.AddSingleton<IDialogContent, ScheduleDialogContent>();
#if WINDOWS
        services.AddSingleton<Barbican.Core.Compose.ComposerClassification>();
        services.AddSingleton<Barbican.App.Plugins.PluginComposeTakeovers>();
        services.AddSingleton<Barbican.Core.Compose.RawMessageSender>();
        services.AddSingleton<Barbican.Core.Plugins.Api.RawSend.IPluginRawSendHost, Barbican.App.Plugins.PluginRawSendHost>();
        services.AddSingleton<IPluginApiMethod>(sp => Barbican.Core.Plugins.Api.RawSend.PluginRawSendMethods.SendRaw(
            sp.GetRequiredService<Barbican.Core.Plugins.Api.RawSend.IPluginRawSendHost>()));
        services.AddSingleton<IPluginApiMethod>(sp => Barbican.Core.Plugins.Api.RawSend.PluginRawSendMethods.SubmitRaw(
            sp.GetRequiredService<Barbican.Core.Plugins.Api.RawSend.IPluginRawSendHost>()));
        services.AddSingleton<Barbican.Core.Plugins.Api.Mail.IPluginMailReaders>(sp => new Barbican.Core.Plugins.Api.Mail.PluginMailReaders(sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<Barbican.Core.Plugins.Api.Mail.IPluginMailRateLimiter>(sp => new Barbican.Core.Plugins.Api.Mail.PluginMailRateLimiter(sp.GetRequiredService<TimeProvider>()));
        for (var index = 0; index < 7; index++)
        {
            var method = index;
            services.AddSingleton<IPluginApiMethod>(sp => Barbican.Core.Plugins.Api.Mail.PluginMailMethods.Create(
                sp.GetRequiredService<Barbican.Core.Plugins.Api.Mail.IPluginMailReaders>(), sp.GetRequiredService<Barbican.Core.Plugins.Api.Mail.IPluginMailRateLimiter>())[method]);
        }
        _ = services.AddBarbicanPluginAccountApi();
        services.AddSingleton<PluginComposerAccess>();
        services.AddSingleton<PluginCheckerRegistry>();
        for (var index = 0; index < 4; index++)
        {
            var method = index;
            services.AddSingleton<IPluginApiMethod>(sp => sp.GetRequiredService<PluginCheckerRegistry>().Methods[method]);
        }
        services.AddSingleton<IComposeLinkAddOns, Barbican.App.Plugins.PluginComposeLinks>();
        services.AddSingleton<Barbican.Core.Compose.IOutgoingAddOnHooks>(sp => new Barbican.App.Plugins.PluginOutgoingSendHooks(
            sp.GetRequiredService<Barbican.App.Plugins.PluginBackgroundRuntimes>(),
            sp.GetRequiredService<Barbican.App.Plugins.PluginPackageHost>(), sp.GetRequiredService<IPluginSessionSource>(),
            () => sp.GetRequiredService<IComposerService>().Current, sp.GetRequiredService<Barbican.Presentation.Common.IDispatcher>(),
            sp.GetRequiredService<Barbican.App.Plugins.PluginComposeTakeovers>()));
        services.AddSingleton<Barbican.App.Plugins.PluginActionRegistration>();
        services.AddSingleton<IPluginApiMethod>(sp => sp.GetRequiredService<Barbican.App.Plugins.PluginActionRegistration>());
        services.AddSingleton<IPluginApiMethod>(sp => PluginComposerMethods.GetSnapshot(sp.GetRequiredService<PluginComposerAccess>()));
        services.AddSingleton<IPluginApiMethod>(sp => PluginComposerMethods.SetSubject(sp.GetRequiredService<PluginComposerAccess>(),
            PluginComposerEditRateLimiter.Create(sp.GetRequiredService<TimeProvider>(), 10)));
        services.AddSingleton<IPluginApiMethod>(sp => PluginComposerMethods.ReadAttachment(sp.GetRequiredService<PluginComposerAccess>()));
#endif

        // a mailto: link clicked in a message opens the composer, never the OS handler (B-COMP-034); everything else keeps the reader's link check
        services.Replace(ServiceDescriptor.Singleton<ILinkRouter>(sp => new ComposerLinkRouter(sp.GetRequiredService<CheckedLinkRouter>(), sp.GetRequiredService<MailtoRouter>())));

        // links from the command line and from later launches, and the hand-off listener
        services.AddSingleton<IStartupTask, MailtoStartup>();
#if AUTOMATION
        _ = Views.Compose.Automation.ComposerAutomation.AddBarbicanComposerAutomation(services);
#endif
        return services;
    }
}
