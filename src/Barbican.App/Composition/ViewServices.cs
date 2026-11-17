using Microsoft.Extensions.DependencyInjection;

namespace Barbican.App.Composition;

/// <summary>
/// Registers the views and the App's IDispatcher (docs/milestones/M1-plan.md §3.3); views get their
/// ViewModels from here, never with <c>new</c> in code-behind. Created empty by stage F1; owned by
/// the shell-ui stream.
/// </summary>
public static class ViewServices
{
    public static IServiceCollection AddBarbicanViews(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
