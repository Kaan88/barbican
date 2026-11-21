// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:238-251,2368-2375 (the keywords.* permission table and routing: list settings:read; add, update, remove, reorder
// settings:write; discover, getCounts, refreshCounts email:read).
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;

namespace Barbican.Core.Plugins.Api.MailTags;

/// <summary>Factory for the keywords.* methods.</summary>
public static class PluginKeywordMethods
{
    public static IReadOnlyList<IPluginApiMethod> Create(PluginTagDefinitions tags, PluginKeywordCounts counts, IPluginMailReaders readers, IPluginMailRateLimiter limiter)
    {
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(counts);
        ArgumentNullException.ThrowIfNull(readers);
        ArgumentNullException.ThrowIfNull(limiter);
        return
        [
            new PluginKeywordsList(tags), new PluginKeywordsAdd(tags), new PluginKeywordsUpdate(tags), new PluginKeywordsRemove(tags), new PluginKeywordsReorder(tags),
            new PluginKeywordsDiscover(readers), new PluginKeywordsGetCounts(counts), new PluginKeywordsRefreshCounts(tags, counts, limiter),
        ];
    }
}
