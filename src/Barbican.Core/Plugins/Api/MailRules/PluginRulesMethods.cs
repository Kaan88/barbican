// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:302-305,2403-2415 (sieve.*: filters:read for isSupported and getActiveScript, filters:write for validateScript and regenerate) and assistant/c.ts:27-29,88-90 (filters.list needs
// filters:read, filters.create and filters.undo need filters:write). One undo-token table is shared by filters.create and filters.undo.
using Barbican.Core.Filters.Rules;
using Barbican.Core.Plugins.Api.Calendar;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Api.MailTags;
using Barbican.Core.Plugins.Bridge;

namespace Barbican.Core.Plugins.Api.MailRules;

/// <summary>Factory for the filters.* and sieve.* methods.</summary>
public static class PluginRulesMethods
{
    /// <summary>How many methods <see cref="Create"/> returns.</summary>
    public const int Count = 7;

    public static IReadOnlyList<IPluginApiMethod> Create(IFilterService filters, PluginTagDefinitions tags, IPluginMailRateLimiter limiter, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(filters);
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(limiter);
        ArgumentNullException.ThrowIfNull(time);
        var undo = new PluginUndoTokens(time);
        return
        [
            new PluginFiltersList(filters), new PluginFiltersCreate(filters, tags, undo, limiter), new PluginFiltersUndo(filters, undo, limiter),
            new PluginSieveIsSupported(), new PluginSieveGetActiveScript(), new PluginSieveValidateScript(limiter), new PluginSieveRegenerate(filters, limiter),
        ];
    }
}
