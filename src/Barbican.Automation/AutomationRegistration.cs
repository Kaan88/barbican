using Microsoft.Extensions.DependencyInjection;

namespace Barbican.Automation;

/// <summary>
/// DI module of the automation build (docs/milestones/M1-plan.md §3.3), called by the composition
/// root only under <c>#if AUTOMATION</c>. The built-in commands live in the server; features add
/// theirs as <see cref="IAutomationCommand"/> registrations in their own files.
/// </summary>
public static class AutomationRegistration
{
    public static IServiceCollection AddBarbicanAutomation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
