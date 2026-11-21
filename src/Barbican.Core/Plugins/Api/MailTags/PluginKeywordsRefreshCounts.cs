// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:1867-1870 doKeywordsRefreshCounts (email:read): counts again, then answers as keywords.getCounts with no ids.
// Deviation: each refresh costs two server queries per tag (see PluginKeywordCounts), so it is limited to 6 a minute for each add-on.
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Api.Account;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.MailTags;

internal sealed class PluginKeywordsRefreshCounts(PluginTagDefinitions tags, PluginKeywordCounts counts, IPluginMailRateLimiter limiter)
    : PluginScopedMethod("keywords.refreshCounts", "email:read", slow: true, limiter: limiter)
{
    protected override string? RateKind => "keywords.refreshCounts";

    protected override int RateLimit => 6;

    public override PluginErrorCode? ValidateArguments(PluginValue arguments) => null;

    protected override async Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        var fresh = await counts.RefreshAsync(context, capture!.Session, tags.Ids(), ct).ConfigureAwait(false);
        if (fresh is null) return await context.CheckCurrentAsync(ct).ConfigureAwait(false) ? PluginCallOutcome.Failed(PluginErrorCode.Failed) : PluginCallOutcome.Failed(PluginErrorCode.StaleContext);
        var b = new PluginValueBuilder();
        return Success(b, PluginKeywordsGetCounts.Project(b, fresh, null));
    }
}
