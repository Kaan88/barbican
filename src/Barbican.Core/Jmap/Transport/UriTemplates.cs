// Mirrors: RFC 8620 §6.1 (uploadUrl {accountId}), §6.2 (downloadUrl {accountId} {blobId} {name} {type}, RFC 6570 level 1, so
// every value is percent-encoded), §7.3 (eventSourceUrl {types} {closeafter} {ping}); Rust jmap.rs:772 download_from (empty name
// is "file", empty type application/octet-stream), :1111 watch (types joined by unescaped commas); Bulwark lib/jmap/client.ts:6876
// getBlobDownloadUrl. Owner jmap.

using Barbican.Core.Jmap.KeywordSyntax;

namespace Barbican.Core.Jmap.Transport;

/// <summary>Expands the session's URL templates.</summary>
public static class UriTemplates
{
    /// <summary>The download URL of a blob.</summary>
    public static string Download(string template, string accountId, string blobId, string name, string type)
    {
        ArgumentNullException.ThrowIfNull(template);
        return template
            .Replace("{accountId}", KeywordRules.PercentEncode(accountId), StringComparison.Ordinal)
            .Replace("{blobId}", KeywordRules.PercentEncode(blobId), StringComparison.Ordinal)
            .Replace("{name}", KeywordRules.PercentEncode(string.IsNullOrEmpty(name) ? "file" : name), StringComparison.Ordinal)
            .Replace("{type}", KeywordRules.PercentEncode(string.IsNullOrEmpty(type) ? "application/octet-stream" : type), StringComparison.Ordinal);
    }

    /// <summary>The upload URL of an account.</summary>
    public static string Upload(string template, string accountId)
    {
        ArgumentNullException.ThrowIfNull(template);
        return template.Replace("{accountId}", KeywordRules.PercentEncode(accountId), StringComparison.Ordinal);
    }

    /// <summary>The event source URL for <paramref name="types"/> (empty: every type, <c>*</c>), <c>closeafter=no</c>, a ping every <paramref name="pingSeconds"/>.</summary>
    public static string EventSource(string template, IReadOnlyList<string> types, int pingSeconds)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(types);
        var t = types.Count == 0 ? "*" : string.Join(",", types.Select(KeywordRules.PercentEncode));
        return template
            .Replace("{types}", t, StringComparison.Ordinal)
            .Replace("{closeafter}", "no", StringComparison.Ordinal)
            .Replace("{ping}", pingSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }
}
