// Mirrors: nothing in Bulwark or Stalwart; this is the jmap feature's DI module (docs/milestones/M1-plan.md §3.3). Owned by the jmap
// stream.

using Barbican.Core.Jmap.Transport;
using Barbican.Core.Mail.Service;
using Barbican.Core.Plugins.Hooks;
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

        // the one hook bus (mailcore's HookBus, M1-plan §4.1). The composition root calls this module first, so a stand-in here would win
        // every later TryAdd and the ViewModels, the sync engine and the plugin hosts (M6b) would talk to different buses. mailcore's
        // registration repeats these two lines with TryAdd, so the order of the modules does not matter.
        services.TryAddSingleton<HookBus>();
        services.TryAddSingleton<IHookBus>(sp => sp.GetRequiredService<HookBus>());
        return services;
    }
}
