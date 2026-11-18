// Mirrors: M4 plan section 4.6 (DI). Owner cal-edit: the invitation banner with its actions (replaces the M2 display-only registration of IReaderBannerSource for invitations) and the calendar-side services' view models it needs.

using Barbican.Core.Accounts;
using Barbican.Core.Calendar.Invitations;
using Barbican.Presentation.Compose;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Reader;
using Microsoft.Extensions.DependencyInjection;

namespace Barbican.Presentation.Composition;

/// <summary>The portable services of M4 stream cal-edit.</summary>
public static class CalendarEditingPresentationRegistration
{
    public static IServiceCollection AddBarbicanCalendarEditingViewModels(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // the invitation banner's buttons: the source itself is registered with the reader (it shows the banner); this stateless twin runs the buttons
        services.AddSingleton<IBannerActionHandler>(sp => new InvitationBannerSource(sp.GetRequiredService<IActiveMail>(), sp.GetRequiredService<IAccountManager>(), sp.GetService<IInvitationService>()));
        return services;
    }
}
