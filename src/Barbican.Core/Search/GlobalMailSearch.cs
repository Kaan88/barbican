// Mirrors: Rust gsearch.rs:473 hit_folder, :487 search_mail (Email/query + Email/get in one request, 25 hits); Bulwark lib/global-search/providers/mail.ts. The contacts, calendar and
// files providers return nothing until M4/M5 (their pure filters, GlobalSearch.FilterFiles, are ported and tested); this is the mail provider.

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Jmap.Requests;

namespace Barbican.Core.Search;

/// <summary>The mail part of "search everything".</summary>
public static class GlobalMailSearch
{
    /// <summary>How many hits one kind brings (Rust REMOTE_LIMIT).</summary>
    public const int RemoteLimit = 25;

    private static readonly string[] HitProperties = ["id", "subject", "from", "receivedAt", "mailboxIds", "preview"];

    /// <summary>The mail hits of a parsed search: Trash and Junk left out unless chosen, newest first, as <see cref="SearchHit"/>s that open in the folder they are in.</summary>
    /// <param name="client">The account's client.</param>
    /// <param name="parsed">The search.</param>
    /// <param name="roles">Role to mailbox id.</param>
    /// <param name="prefer">The folders a hit opens in when it is in them (the open folder, the Inbox).</param>
    /// <param name="folderName">The name of a folder id, for the second line.</param>
    /// <param name="ct">Cancellation.</param>
    public static async Task<Result<IReadOnlyList<SearchHit>>> RunAsync(
        IJmapClient client, ParsedSearch parsed, IReadOnlyDictionary<string, string> roles, IReadOnlyList<string> prefer, Func<string, string> folderName, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(parsed);
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(folderName);
        var acc = client.Session.MailAccountId;
        var filter = GlobalSearch.GlobalMailFilter(parsed, r => roles.TryGetValue(r, out var id) ? id : null);
        var query = new JsonObject
        {
            ["accountId"] = acc,
            ["filter"] = filter,
            ["sort"] = new JsonArray(new JsonObject { ["property"] = "receivedAt", ["isAscending"] = false }),
            ["limit"] = RemoteLimit,
        };
        var get = new JsonObject
        {
            ["accountId"] = acc,
            ["#ids"] = new JsonObject { ["resultOf"] = "q", ["name"] = "Email/query", ["path"] = "/ids" },
            ["properties"] = new JsonArray([.. HitProperties.Select(p => (JsonNode?)JsonValue.Create(p))]),
        };
        var r = await client.CallAsync(JmapRequest.Of(JmapRequest.Call("Email/query", query, "q"), JmapRequest.Call("Email/get", get, "g")), ct).ConfigureAwait(false);
        if (!r.IsOk)
        {
            return Result<IReadOnlyList<SearchHit>>.Fail(r.Error!);
        }

        var hits = new List<SearchHit>();
        foreach (var m in r.Value!.MethodResponses[1][1].Arr("list")?.OfType<JsonObject>() ?? [])
        {
            var folder = GlobalSearch.HitFolder(m, prefer);
            var from = m["from"] is JsonArray { Count: > 0 } fa ? (fa[0].Str("name") is { Length: > 0 } n ? n : fa[0].Str("email") ?? string.Empty) : string.Empty;
            var name = folderName(folder);
            hits.Add(new SearchHit(
                SearchScope.Mail,
                m.Str("id") ?? string.Empty,
                m.Str("subject") is { Length: > 0 } s ? s : "(no subject)",
                string.Join(" · ", new[] { from, name }.Where(x => x.Length > 0)),
                m.Str("receivedAt") ?? string.Empty,
                folder));
        }

        return Result<IReadOnlyList<SearchHit>>.Ok(hits);
    }
}
