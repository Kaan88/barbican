// Mirrors: Rust search_ui.rs:145 fetch_snippets (SearchSnippet/get for the page just loaded, with the loosened filter); Bulwark lib/search-snippet.ts:46-161
// (getSearchSnippets); RFC 8621 section 5; Stalwart crates/jmap/src/email/snippet.rs. J-056.

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Jmap.Requests;
using Barbican.Core.Mail.Unified;

namespace Barbican.Core.Search;

/// <summary>Asks the server which words of a page of messages the search hit.</summary>
public static class SearchSnippetFetcher
{
    /// <summary>
    /// The snippets of list ids for the filter: plain ids are asked of <paramref name="active"/> (the account on screen), a unified row id of the
    /// login and JMAP account it names (the shared account, else that login's mail account) with its server id, and its entry carries the row id
    /// again (Fable review 3 R3-01). A source whose login is not connected, or whose server refuses, gets no snippets; the others still do.
    /// </summary>
    public static async Task<Result<IReadOnlyList<SnippetEntry>>> FetchRowsAsync(
        IJmapClient? active, Func<string, IJmapClient?> clientOf, JsonNode filter, IReadOnlyList<string> rowIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(clientOf);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(rowIds);
        var plain = rowIds.Where(id => UnifiedRows.ParseRow(id) is null).ToList();
        var res = new List<SnippetEntry>();
        if (plain.Count > 0 && active is not null)
        {
            var r = await FetchAsync(active, filter, plain, ct).ConfigureAwait(false);
            if (!r.IsOk)
            {
                return r;
            }

            res.AddRange(r.Value!);
        }

        foreach (var source in rowIds.Select(id => (Id: id, Row: UnifiedRows.ParseRow(id))).Where(x => x.Row is not null).GroupBy(x => (x.Row!.Login, x.Row.SharedAccount)))
        {
            if (clientOf(source.Key.Login) is not { } client)
            {
                continue;
            }

            var byReal = source.ToDictionary(x => x.Row!.Id, x => x.Id, StringComparer.Ordinal);
            var r = await FetchAsync(client, source.Key.SharedAccount, filter, [.. byReal.Keys], ct).ConfigureAwait(false);
            if (r.IsOk)
            {
                res.AddRange(r.Value!.Where(e => byReal.ContainsKey(e.EmailId)).Select(e => e with { EmailId = byReal[e.EmailId] }));
            }
        }

        return Result<IReadOnlyList<SnippetEntry>>.Ok(res);
    }

    /// <summary>The snippets of <paramref name="emailIds"/> (server ids of the login's mail account) for the filter (nothing is asked when the
    /// filter has no text term).</summary>
    public static Task<Result<IReadOnlyList<SnippetEntry>>> FetchAsync(IJmapClient client, JsonNode filter, IReadOnlyList<string> emailIds, CancellationToken ct) =>
        FetchAsync(client, null, filter, emailIds, ct);

    /// <summary>As <see cref="FetchAsync(IJmapClient, JsonNode, IReadOnlyList{string}, CancellationToken)"/> for the JMAP account
    /// <paramref name="accountId"/> (a shared account); null for the login's own mail account.</summary>
    public static async Task<Result<IReadOnlyList<SnippetEntry>>> FetchAsync(IJmapClient client, string? accountId, JsonNode filter, IReadOnlyList<string> emailIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(emailIds);
        if (emailIds.Count == 0 || !GlobalSearch.HasSnippetTerms(filter))
        {
            return Result<IReadOnlyList<SnippetEntry>>.Ok([]);
        }

        var res = new List<SnippetEntry>();
        foreach (var chunk in emailIds.Chunk(Math.Max(1, client.Session.Limits.MaxObjectsInGet)))
        {
            var args = new JsonObject
            {
                ["accountId"] = accountId ?? client.Session.MailAccountId,
                ["filter"] = GlobalSearch.SnippetFilter(filter),
                ["emailIds"] = new JsonArray([.. chunk.Select(id => (JsonNode?)JsonValue.Create(id))]),
            };
            var r = await client.CallAsync(JmapRequest.Of(JmapRequest.Call("SearchSnippet/get", args, "s")), ct).ConfigureAwait(false);
            if (!r.IsOk)
            {
                return Result<IReadOnlyList<SnippetEntry>>.Fail(r.Error!);
            }

            foreach (var e in r.Value!.MethodResponses[0][1].Arr("list")?.OfType<JsonObject>() ?? [])
            {
                if (e.Str("emailId") is { } id)
                {
                    res.Add(new SnippetEntry(id, e.Str("subject"), e.Str("preview")));
                }
            }
        }

        return Result<IReadOnlyList<SnippetEntry>>.Ok(res);
    }
}
