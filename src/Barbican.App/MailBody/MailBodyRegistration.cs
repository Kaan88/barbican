// Mirrors: docs/milestones/M1-plan.md §3.3 (one registration module per feature; the completing stream replaces a placeholder with Replace),
// §6.5 (MessageLoader replaces mailcore's header-only IMessageOpener). Owner body.

using Barbican.Core.Mail.Body;
using Barbican.Core.Mail.Cache;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Settings.Accessibility;
using Barbican.Core.Storage;
using Barbican.Presentation.Common;
using Barbican.Presentation.Composition;
using Barbican.Presentation.Input;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Mail.Loading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Barbican.App.MailBody;

/// <summary>Registers the mail body view, the message loader and prefetch.</summary>
public static class MailBodyRegistration
{
    public static IServiceCollection AddBarbicanMailBody(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // M1 link router placeholder (opens nothing, logs the scheme); M2 replaces it
        services.TryAddSingleton<ILinkRouter, LoggingLinkRouter>();

        // the body cache is the accounts stream's; a run without it (tests of the composition) still gets one
        services.TryAddSingleton<IBodyCache>(_ => new BodyCache());

        // loading: fetch through the cache, prefetch in the direction of travel, never write (M1 is read-only)
        services.TryAddSingleton<IBlobSource, ActiveBlobSource>();
        services.TryAddSingleton<IMailActions, ReadOnlyMailActions>();
        services.TryAddSingleton(sp => new MessageFetcher(sp.GetRequiredService<IBodyCache>(), sp.GetRequiredService<IBlobSource>()));
        services.TryAddSingleton(sp => new MessagePrefetcher(sp.GetRequiredService<MessageFetcher>()));
        services.TryAddSingleton(sp => new ListNeighbours(sp));
        services.TryAddSingleton<IMessageNeighbours>(sp => sp.GetRequiredService<ListNeighbours>());
        services.AddSingleton<IStartupTask>(sp => sp.GetRequiredService<ListNeighbours>());

        // the body view: one warm WebView2 shell, started with the window
        services.TryAddSingleton<BodyAppearance>();
        services.TryAddSingleton(sp => ActivatorUtilities.CreateInstance<BodyShell>(sp, new Lazy<IKeyRouter>(sp.GetRequiredService<IKeyRouter>)));
        services.Replace(ServiceDescriptor.Singleton<IBodyPresenter>(sp => sp.GetRequiredService<BodyShell>()));
        services.AddSingleton<IStartupTask>(sp => sp.GetRequiredService<BodyShell>());
#if WINDOWS
        services.TryAddSingleton<IBodyOverlayStrategy, InPlaceBodyOverlay>();
#else
        services.TryAddSingleton<IBodyOverlayStrategy, X11BodyOverlay>();
#endif

        // the loader replaces mailcore's header-only opener
        services.TryAddSingleton(sp =>
        {
            var prefs = sp.GetRequiredService<IPrefsStore>();
            var system = sp.GetRequiredService<ISystemAccessibilitySource>();
            var time = sp.GetRequiredService<TimeProvider>();
            return new MessageLoader(
                sp.GetRequiredService<ReaderViewModel>(),
                sp.GetRequiredService<IActiveMail>(),
                sp.GetRequiredService<MessageFetcher>(),
                sp.GetRequiredService<MessagePrefetcher>(),
                sp.GetRequiredService<IBodyPresenter>(),
                sp.GetRequiredService<IMessageNeighbours>(),
                sp.GetRequiredService<IMailActions>(),
                sp.GetRequiredService<IHookBus>(),
                sp.GetRequiredService<IDispatcher>(),
                time,
                prefs,
                () => MailListSettings.Read(prefs.Current, system.Current).ReaderOptionsAt(time.GetUtcNow(), time.LocalTimeZone),
                sp.GetService<ReaderDepth>());
        });
        services.Replace(ServiceDescriptor.Singleton<IMessageOpener>(sp => sp.GetRequiredService<MessageLoader>()));
        services.TryAddSingleton<IConversationOpener>(sp => sp.GetRequiredService<MessageLoader>());

#if AUTOMATION
        services.AddSingleton<Barbican.Automation.IAutomationCommand, Automation.BodyCommand>();
        services.AddSingleton<Barbican.Automation.IAutomationCommand, Automation.ThreadCommand>();
#endif
        return services;
    }
}
