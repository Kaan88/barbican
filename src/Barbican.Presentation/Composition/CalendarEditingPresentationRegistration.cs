// Mirrors: M4 plan section 4.6 (DI). Owner cal-edit: the invitation banner with its actions (replaces the M2 display-only registration of IReaderBannerSource for invitations) and the calendar-side services' view models it needs.

using Microsoft.Extensions.DependencyInjection;

namespace Barbican.Presentation.Composition;

/// <summary>The portable services of M4 stream cal-edit.</summary>
public static class CalendarEditingPresentationRegistration
{
    public static IServiceCollection AddBarbicanCalendarEditingViewModels(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
