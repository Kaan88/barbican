// Mirrors: M3 plan section 4.7. Owner composer-ui: the composer shell views, recipient chips, dialogs, the compose commands and shortcuts, mailto and single-instance start-up; the view models come from M3PresentationRegistration.

using Barbican.App.Views.Compose;
using Barbican.App.Views.Dialogs;
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
        services.AddSingleton<IDialogContent, AccountPickDialogContent>();
        services.AddSingleton<IDialogContent, ScheduleDialogContent>();

        // a mailto: link clicked in a message opens the composer, never the OS handler (B-COMP-034); everything else keeps the reader's link check
        services.Replace(ServiceDescriptor.Singleton<ILinkRouter>(sp => new ComposerLinkRouter(sp.GetRequiredService<CheckedLinkRouter>(), sp.GetRequiredService<MailtoRouter>())));

        // links from the command line and from later launches, and the hand-off listener
        services.AddSingleton<IStartupTask, MailtoStartup>();
#if AUTOMATION
        services.AddBarbicanComposerAutomation();
#endif
        return services;
    }
}
