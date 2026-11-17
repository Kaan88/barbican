// Mirrors: Rust search_ui.rs:145 fetch_snippets (SearchSnippet/get for the page just loaded, with the loosened filter); Bulwark lib/search-snippet.ts:46-161
// (getSearchSnippets); RFC 8621 section 5; Stalwart crates/jmap/src/email/snippet.rs. J-056.

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Jmap.Requests;

namespace Barbican.Core.Search;

/// <summary>Asks the server which words of a page of messages the search hit.</summary>
public static class SearchSnippetFetcher
{
    /// <summary>The snippets of <paramref name="emailIds"/> for the filter (nothing is asked when the filter has no text term).</summary>
    public static async Task<Result<IReadOnlyList<SnippetEntry>>> FetchAsync(IJmapClient client, JsonNode filter, IReadOnlyList<string> emailIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(emailIds);
        if (!GlobalSearch.HasSnippetTerms(filter))
        {
            return Result<IReadOnlyList<SnippetEntry>>.Ok([]);
        }

        // a unified row id names another account (or the same one through its row id): SearchSnippet/get would answer notFound for it, so such
        // rows get no snippet (Fable review 3 R3-01)
        emailIds = [.. emailIds.Where(id => Mail.Unified.UnifiedRows.ParseRow(id) is null)];
        if (emailIds.Count == 0)
        {
            return Result<IReadOnlyList<SnippetEntry>>.Ok([]);
        }

        var res = new List<SnippetEntry>();
        foreach (var chunk in emailIds.Chunk(Math.Max(1, client.Session.Limits.MaxObjectsInGet)))
        {
            var args = new JsonObject
            {
                ["accountId"] = client.Session.MailAccountId,
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
