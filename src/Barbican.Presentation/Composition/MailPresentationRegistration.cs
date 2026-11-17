using Microsoft.Extensions.DependencyInjection;

namespace Barbican.Presentation.Composition;

/// <summary>
/// Registers the mail ViewModels, ListSync, the key router and the hook bus
/// (docs/milestones/M1-plan.md §3.3). Created empty by stage F1 so the composition root compiles
/// before the mailcore stream exists; owned by the mailcore stream.
/// </summary>
public static class MailPresentationRegistration
{
    public static IServiceCollection AddBarbicanMailCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
