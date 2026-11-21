// Mirrors: Rust gsearch.rs:473 hit_folder, :487 search_mail (Email/query + Email/get in one request, 25 hits); Bulwark lib/global-search/providers/mail.ts and lib/search-snippet.ts:46 (the hits carry the server's SearchSnippet marks for the palette preview).
// The contacts, calendar and files hits come from the stores (GlobalLocalSearch); this is the mail provider.

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

    private static readonly string[] HitProperties = ["id", "subject", "from", "to", "receivedAt", "mailboxIds", "preview"];

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

        // the server marks the words that matched (SearchSnippet/get, Bulwark lib/search-snippet.ts:46); a refusal leaves the hits plain
        var rows = r.Value!.MethodResponses[1][1].Arr("list")?.OfType<JsonObject>().ToList() ?? [];
        var snippets = new Dictionary<string, SnippetEntry>(StringComparer.Ordinal);
        if (rows.Count > 0 && GlobalSearch.HasSnippetTerms(filter))
        {
            var asked = await SearchSnippetFetcher.FetchAsync(client, filter, [.. rows.Select(m => m.Str("id") ?? string.Empty)], ct).ConfigureAwait(false);
            foreach (var e in asked.IsOk ? asked.Value! : [])
            {
                snippets[e.EmailId] = e;
            }
        }

        var hits = rows.Select(m => ToHit(m, snippets.GetValueOrDefault(m.Str("id") ?? string.Empty), prefer, folderName)).ToList();
        return Result<IReadOnlyList<SearchHit>>.Ok(hits);
    }

    private static SearchHit ToHit(JsonObject m, SnippetEntry? snip, IReadOnlyList<string> prefer, Func<string, string> folderName)
    {
        var folder = GlobalSearch.HitFolder(m, prefer);
        var from = m["from"] is JsonArray { Count: > 0 } fa ? (fa[0].Str("name") is { Length: > 0 } n ? n : fa[0].Str("email") ?? string.Empty) : string.Empty;
        var name = folderName(folder);
        var details = new List<SearchDetail>();
        if (AddressList(m, "from") is { Length: > 0 } fromLine)
        {
            details.Add(new SearchDetail("mail", fromLine));
        }

        if (AddressList(m, "to") is { Length: > 0 } toLine)
        {
            details.Add(new SearchDetail("people", "To " + toLine));
        }

        if (name.Length > 0)
        {
            details.Add(new SearchDetail("folder", name));
        }

        var received = m.Str("receivedAt") ?? string.Empty;
        if (received.Length > 0)
        {
            details.Add(new SearchDetail("clock", GlobalLocalSearch.FormatInstant(received)));
        }

        var marked = snip?.Preview is { Length: > 0 } sp ? GlobalSearch.SnippetSegments(sp) : [];
        var markedSubject = snip?.Subject is { Length: > 0 } ss ? GlobalSearch.SnippetSegments(ss) : [];
        return new SearchHit(
            SearchScope.Mail,
            m.Str("id") ?? string.Empty,
            m.Str("subject") is { Length: > 0 } s ? s : "(no subject)",
            string.Join(" · ", new[] { from, name }.Where(x => x.Length > 0)),
            received,
            folder)
        {
            Details = details,
            Snippet = GlobalSearch.HasMarked(marked) ? marked : [],
            SubjectSnippet = GlobalSearch.HasMarked(markedSubject) ? markedSubject : [],
            Body = m.Str("preview") ?? string.Empty,
        };
    }

    // "Name <address>, ..." of an address list property
    private static string AddressList(JsonObject message, string property) =>
        message[property] is JsonArray list
            ? string.Join(", ", list.OfType<JsonObject>().Select(a => a.Str("name") is { Length: > 0 } n ? $"{n} <{a.Str("email")}>" : a.Str("email") ?? string.Empty).Where(x => x.Length > 0))
            : string.Empty;
}
