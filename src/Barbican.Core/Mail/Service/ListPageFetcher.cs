// Mirrors: Rust jmap.rs:524 list_page (Email/query, then Email/get of /ids and Thread/get of /list/*/threadId in one request; an
// anchor the server no longer finds falls back to the position; a page larger than maxObjectsInGet reads the ids and the states in one
// request and the rows in chunks), :1564 thread_sizes; RFC 8620 §5.5 (query: position, anchor, anchorOffset, calculateTotal,
// canCalculateChanges), §3.7 (result references); Bulwark lib/jmap/client.ts:3085 getEmails (rows in query order). Owner jmap.

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Jmap.Requests;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Mail.Service;

/// <summary>Reads one page of a list with the states that keep it current.</summary>
public static class ListPageFetcher
{
    /// <summary>The page of <paramref name="filter"/> after <paramref name="after"/> (null: the first page).</summary>
    public static async Task<Result<ListPage>> FetchAsync(IJmapClient client, JsonObject filter, ListCursor? after, int limit, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(filter);
        var position = after?.Position ?? 0;
        var page = limit > client.Session.Limits.MaxObjectsInGet
            ? await ChunkedAsync(client, filter, position, after?.AnchorId, limit, ct).ConfigureAwait(false)
            : await OneRequestAsync(client, filter, position, after?.AnchorId, limit, ct).ConfigureAwait(false);
        if (!page.IsOk && !string.IsNullOrEmpty(after?.AnchorId) && page.Error is MethodError { Type: "anchorNotFound" })
        {
            // the anchor was deleted or moved out: read from where it stood (Rust jmap.rs:541)
            return await FetchAsync(client, filter, after with { AnchorId = "" }, limit, ct).ConfigureAwait(false);
        }

        return page;
    }

    /// <summary>Conversation sizes by thread id.</summary>
    public static IDictionary<string, int> ThreadSizes(IEnumerable<JsonNode?> threads)
    {
        var sizes = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var t in threads)
        {
            if (t.Str("id") is { } id && t.Arr("emailIds") is { } ids)
            {
                sizes[id] = ids.Count;
            }
        }

        return sizes;
    }

    /// <summary>Rows read into summaries, each with its conversation size.</summary>
    public static IReadOnlyList<EmailSummary> Rows(IEnumerable<JsonNode?> list, IDictionary<string, int> threads) =>
        [.. list.Select(ModelReader.Summary).OfType<EmailSummary>()
            .Select(e => e.ThreadId is { } t && threads.TryGetValue(t, out var n) ? e with { ThreadCount = n } : e)];

    private static JsonObject Query(IJmapClient client, JsonObject filter, int position, string? anchor, int limit)
    {
        var q = new JsonObject
        {
            ["accountId"] = client.Session.MailAccountId,
            ["filter"] = filter.DeepClone(),
            ["sort"] = JmapProperties.ListSort(),
            ["limit"] = limit,
            ["calculateTotal"] = true,
        };
        if (string.IsNullOrEmpty(anchor))
        {
            q["position"] = position;
        }
        else
        {
            q["anchor"] = anchor;
            q["anchorOffset"] = 1;
        }

        return q;
    }

    private static async Task<Result<ListPage>> OneRequestAsync(
        IJmapClient client, JsonObject filter, int position, string? anchor, int limit, CancellationToken ct)
    {
        var acc = client.Session.MailAccountId;
        var b = new JmapRequestBuilder();
        var q = b.Add("Email/query", Query(client, filter, position, anchor, limit));
        var g = b.Add("Email/get", new JsonObject { ["accountId"] = acc, ["#ids"] = q.Ref("/ids"), ["properties"] = JmapProperties.Json(JmapProperties.EmailList) });
        var t = b.Add("Thread/get", new JsonObject { ["accountId"] = acc, ["#ids"] = g.Ref("/list/*/threadId") });
        var sent = await client.SendAsync(b.Build(), ct).ConfigureAwait(false);
        if (!sent.IsOk)
        {
            return Result<ListPage>.Fail(sent.Error!);
        }

        var r = sent.Value!;
        var qa = r.Args(q.CallId, q.Method);
        var ga = r.Args(g.CallId, g.Method);
        var ta = r.Args(t.CallId, t.Method);
        if (!qa.IsOk || !ga.IsOk || !ta.IsOk)
        {
            return Result<ListPage>.Fail((qa.Error ?? ga.Error ?? ta.Error)!);
        }

        var threads = ThreadSizes(ta.Value!.Arr("list") ?? []);
        return Result<ListPage>.Ok(Page(qa.Value!, Rows(ga.Value!.Arr("list") ?? [], threads), qa.Value!.Strings("ids"), threads, ga.Value!.Str("state"), ta.Value!.Str("state"), limit));
    }

    private static async Task<Result<ListPage>> ChunkedAsync(
        IJmapClient client, JsonObject filter, int position, string? anchor, int limit, CancellationToken ct)
    {
        var acc = client.Session.MailAccountId;
        var b = new JmapRequestBuilder();
        var q = b.Add("Email/query", Query(client, filter, position, anchor, limit));
        var e = b.Add("Email/get", new JsonObject { ["accountId"] = acc, ["ids"] = new JsonArray(), ["properties"] = new JsonArray("id") });
        var t = b.Add("Thread/get", new JsonObject { ["accountId"] = acc, ["ids"] = new JsonArray() });
        var sent = await client.SendAsync(b.Build(), ct).ConfigureAwait(false);
        var qa = sent.IsOk ? sent.Value!.Args(q.CallId, q.Method) : Result<JsonObject>.Fail(sent.Error!);
        if (!qa.IsOk)
        {
            return Result<ListPage>.Fail(qa.Error!);
        }

        var ids = qa.Value!.Strings("ids");
        var rows = await client.GetAllAsync("Email/get", acc, ids, JmapProperties.Json(JmapProperties.EmailList), ct).ConfigureAwait(false);
        if (!rows.IsOk)
        {
            return Result<ListPage>.Fail(rows.Error!);
        }

        var order = ids.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i, StringComparer.Ordinal);
        var sorted = rows.Value!.OrderBy(m => order.TryGetValue(m.Str("id") ?? "", out var i) ? i : int.MaxValue).ToList();
        var tids = sorted.Select(m => m.Str("threadId")).OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var th = await client.GetAllAsync("Thread/get", acc, tids, null, ct).ConfigureAwait(false);
        if (!th.IsOk)
        {
            return Result<ListPage>.Fail(th.Error!);
        }

        var threads = ThreadSizes(th.Value!);
        var r = sent.Value!;
        return Result<ListPage>.Ok(Page(qa.Value!, Rows(sorted, threads), ids, threads, r.ByCallId(e.CallId)?[1].Str("state"), r.ByCallId(t.CallId)?[1].Str("state"), limit));
    }

    internal static ListPage Page(JsonObject q, IReadOnlyList<EmailSummary> rows, IReadOnlyList<string> ids, IDictionary<string, int> threads, string? emailState, string? threadState, int limit)
    {
        var position = q.Count("position") ?? 0;
        var total = q["total"].AsNumber() is { } n ? (int?)n : null;
        // "more pages" counts the ids the query returned, not the rows that parsed: one odd message must not end paging (Fable F-14)
        var more = ids.Count >= limit && (total is null || position + ids.Count < total);
        var parsed = rows.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        var dropped = ids.Where(id => !parsed.Contains(id)).ToList();
        return new ListPage
        {
            Items = rows,
            Total = total,
            Position = position,
            Threads = threads.AsReadOnly(),
            QueryState = q.Str("queryState") ?? "",
            EmailState = emailState ?? "",
            ThreadState = threadState ?? "",
            CanCalculateChanges = q.True("canCalculateChanges"),
            DroppedIds = dropped,
            Next = more && ids.Count > 0 ? new ListCursor(ids[^1]) { Position = position + ids.Count } : null,
        };
    }
}
