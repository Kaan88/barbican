// Mirrors: nothing in Bulwark or Stalwart; this is the jmap feature's DI module (docs/milestones/M1-plan.md §3.3). Owned by the jmap
// stream.

using Barbican.Core.Jmap.Transport;
using Barbican.Core.Mail.Service;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Sync;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Barbican.Core.Jmap;

/// <summary>Registers the JMAP core: transport, session, requests, methods, sync, push.</summary>
public static class JmapRegistration
{
    public static IServiceCollection AddBarbicanCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IJmapHttp, JmapHttp>();
        services.TryAddSingleton<IJmapClientFactory, JmapClientFactory>();
        services.TryAddSingleton<IMailAccountFactory, MailAccountFactory>();

        // stands in until the plugin host registers the real bus (mailcore); a later AddSingleton<IHookBus> wins
        services.TryAddSingleton<IHookBus>(NullHookBus.Instance);
        return services;
    }
}
