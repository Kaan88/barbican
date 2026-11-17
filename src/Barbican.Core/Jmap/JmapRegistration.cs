// Mirrors: nothing in Bulwark or Stalwart; this is the jmap feature's DI module
// (docs/milestones/M1-plan.md §3.3). Created empty by stage F1 so the composition root compiles
// before the jmap stream exists; owned by the jmap stream.
using Microsoft.Extensions.DependencyInjection;

namespace Barbican.Core.Jmap;

/// <summary>Registers the JMAP core: transport, session, requests, methods, sync, push.</summary>
public static class JmapRegistration
{
    public static IServiceCollection AddBarbicanCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
