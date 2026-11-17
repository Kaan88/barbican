using Barbican.Core.Mail.Body;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Barbican.App.MailBody;

/// <summary>
/// Registers the mail body view, the message loader and prefetch (docs/milestones/M1-plan.md §3.3).
/// Stage F1 registers only the M1 link router placeholder (opens nothing, logs the scheme), with
/// TryAdd so M2 can replace it. Owned by the body stream.
/// </summary>
public static class MailBodyRegistration
{
    public static IServiceCollection AddBarbicanMailBody(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<ILinkRouter, LoggingLinkRouter>();
        return services;
    }
}
