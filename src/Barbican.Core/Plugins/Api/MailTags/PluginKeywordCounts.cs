// Mirrors: Bulwark stores/email-store.ts fetchTagCounts and host-api.ts:1822-1870 doKeywordsGetCounts / doKeywordsRefreshCounts: per tag id { total, unread },
// getCounts reading what was last counted, refreshCounts counting first. Bulwark's store counts in the background; Barbican has no tag-count service,
// so a refresh counts each defined tag with two Email/query calls (RFC 8621 section 4.4, `hasKeyword: $label:<id>` with and without `notKeyword: $seen`,
// total only) through the login's mail service, for at most 100 tags (the first 100 definitions). Counts are held per login, in memory only.
using Barbican.Core.Accounts;
using Barbican.Core.Mail.Model;
using Barbican.Core.Plugins.Bridge;

namespace Barbican.Core.Plugins.Api.MailTags;

/// <summary>The last counted tag totals per login.</summary>
public sealed class PluginKeywordCounts
{
    internal const int MaxCounted = 100;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, IReadOnlyDictionary<string, (int Total, int Unread)>> _byAccount = new(StringComparer.Ordinal);

    internal IReadOnlyDictionary<string, (int Total, int Unread)> Get(string accountKey)
    {
        lock (_gate) return _byAccount.TryGetValue(accountKey, out var counts) ? counts : new Dictionary<string, (int, int)>(StringComparer.Ordinal);
    }

    /// <summary>Counts the tags and keeps the result; null when the call went stale or the server could not count (nothing is kept then).</summary>
    internal async Task<IReadOnlyDictionary<string, (int Total, int Unread)>?> RefreshAsync(IPluginInvocationContext context, IAccountSession session, IReadOnlyList<string> tagIds, CancellationToken ct)
    {
        var counts = new Dictionary<string, (int Total, int Unread)>(StringComparer.Ordinal);
        foreach (var id in tagIds.Take(MaxCounted))
        {
            var keyword = "$label:" + id;
            var total = await session.Mail.QueryAsync(new ListQuery { HasKeyword = keyword, PageSize = 1 }, null, ct).ConfigureAwait(false);
            if (!total.IsOk || !await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return null;
            var unread = await session.Mail.QueryAsync(new ListQuery { HasKeyword = keyword, NotKeyword = "$seen", PageSize = 1 }, null, ct).ConfigureAwait(false);
            if (!unread.IsOk || !await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return null;
            counts[id] = (total.Value!.Total ?? total.Value.Items.Count, unread.Value!.Total ?? unread.Value.Items.Count);
        }

        lock (_gate) _byAccount[session.Account.Key] = counts;
        return counts;
    }
}
