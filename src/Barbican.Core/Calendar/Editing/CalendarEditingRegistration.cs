// Mirrors: M4 plan section 4.6 (DI). Owner cal-edit: writers, calendar admin, availability, contact cards, import, subscriptions, invitations.

using Barbican.Core.Calendar.Feeds;
using Barbican.Core.Calendar.Invitations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Barbican.Core.Calendar.Editing;

/// <summary>Registers the event, task, import, subscription and invitation services (M4 stream cal-edit).</summary>
public static class CalendarEditingRegistration
{
    public static IServiceCollection AddBarbicanCalendarEditing(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<ICalendarWriter, CalendarWriter>();
        services.TryAddSingleton<ICalendarAdmin, CalendarAdmin>();
        services.TryAddSingleton<IAvailabilityService, AvailabilityService>();
        services.TryAddSingleton<IContactCardReader, ContactCardReader>();
        services.TryAddSingleton<IIcsImporter, IcsImporter>();
        services.TryAddSingleton<ICalendarSubscriptions, CalendarSubscriptions>();
        services.TryAddSingleton<IInvitationService, InvitationService>();
        return services;
    }
}
