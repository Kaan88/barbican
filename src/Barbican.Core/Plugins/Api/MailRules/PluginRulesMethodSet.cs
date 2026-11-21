// Composition helper: one set of filters.* and sieve.* adapters per host (one undo-token table), registered method by method in the app's container.
using Barbican.Core.Filters.Rules;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Api.MailTags;
using Barbican.Core.Plugins.Bridge;

namespace Barbican.Core.Plugins.Api.MailRules;

/// <summary>The filters.* and sieve.* adapters of one host.</summary>
public sealed class PluginRulesMethodSet(IFilterService filters, PluginTagDefinitions tags, IPluginMailRateLimiter limiter, TimeProvider time)
{
    public IReadOnlyList<IPluginApiMethod> Methods { get; } = PluginRulesMethods.Create(filters, tags, limiter, time);
}
