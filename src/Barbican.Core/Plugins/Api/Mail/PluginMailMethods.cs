// Mirrors: Bulwark lib/plugin-sandbox/host-api-mail.ts and assistant/b.ts (email.get/list/search/getThread/getThreadCount, mailboxes.list) and
// host-api.ts:156 (jmap.getKeywords). Scoped, read-only mail methods: every one needs email:read, projects named fields only and drops its
// answer when the grant, add-on, login or session generation changed before delivery.
using Barbican.Core.Plugins.Bridge;

namespace Barbican.Core.Plugins.Api.Mail;

/// <summary>Factory for the scoped mail read adapters.</summary>
public static class PluginMailMethods
{
    /// <summary>The seven adapters sharing one reader factory and one rate-limit table.</summary>
    public static IReadOnlyList<IPluginApiMethod> Create(IPluginMailReaders readers, IPluginMailRateLimiter limiter)
    {
        ArgumentNullException.ThrowIfNull(readers);
        ArgumentNullException.ThrowIfNull(limiter);
        PluginMailMethod[] methods =
        [
            new PluginEmailGet(readers), new PluginEmailList(readers), new PluginEmailSearch(readers), new PluginEmailGetThread(readers),
            new PluginEmailGetThreadCount(readers), new PluginMailboxesList(readers), new PluginJmapGetKeywords(readers),
        ];
        foreach (var method in methods) method.Limiter = limiter;
        return methods;
    }
}
