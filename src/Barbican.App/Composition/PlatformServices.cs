using Microsoft.Extensions.DependencyInjection;

namespace Barbican.App.Composition;

/// <summary>
/// Registers the platform services of the running head (docs/milestones/M1-plan.md §3.3): on the
/// WinUI head the Barbican.Platform.Windows implementations, on the desktop head the
/// Barbican.Platform.Linux ones. Created empty by stage F1; owned by the accounts stream.
/// </summary>
public static class PlatformServices
{
    public static IServiceCollection AddBarbicanPlatform(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
