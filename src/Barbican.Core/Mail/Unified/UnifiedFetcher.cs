// Mirrors: Rust unified.rs:283 mailboxes_of, :290 inbox_of (by role, else a folder named Inbox), :298 source_page (Email/query of the source's
// inbox, optionally narrowed by text, sorted by receivedAt, with Email/get and Thread/get in one request, every call naming the source's
// account), :326 tag_row (the row id carries login and shared account; the server id stays recoverable). RFC 8620 §3.7 (result references),
// §5.5 (query). Owner unified.

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Jmap.Requests;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Service;

namespace Barbican.Core.Mail.Unified;

/// <summary>Reads pages of the sources' inboxes.</summary>
public static class UnifiedFetcher
{
    /// <summary>The mailboxes of a source's account (id, name, role, counts, rights).</summary>
    public static async Task<Result<IReadOnlyList<JsonNode>>> MailboxesAsync(UnifiedSource source, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        var b = new JmapRequestBuilder();
        var get = b.Add("Mailbox/get", new JsonObject
        {
            ["accountId"] = source.AccountId,
            ["ids"] = null,
            ["properties"] = new JsonArray("id", "name", "role", "parentId", "unreadEmails", "totalEmails", "myRights"),
        });
        var sent = await source.Client.SendAsync(b.Build(), ct).ConfigureAwait(false);
        var args = sent.IsOk ? sent.Value!.Args(get.CallId, get.Method) : Result<JsonObject>.Fail(sent.Error!);
        return args.IsOk
            ? Result<IReadOnlyList<JsonNode>>.Ok([.. (args.Value!.Arr("list") ?? []).OfType<JsonNode>()])
            : Result<IReadOnlyList<JsonNode>>.Fail(args.Error!);
    }

    /// <summary>One page of a source's inbox from <paramref name="position"/>.</summary>
    public static async Task<Result<UnifiedSourcePage>> PageAsync(UnifiedSource source, ListQuery query, int position, int limit, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(query);
        var boxes = await MailboxesAsync(source, ct).ConfigureAwait(false);
        if (!boxes.IsOk)
        {
            return Result<UnifiedSourcePage>.Fail(boxes.Error!);
        }

        if (UnifiedRows.InboxOf(boxes.Value!) is not { } inbox || inbox.Str("id") is not { } inboxId)
        {
            return Result<UnifiedSourcePage>.Fail(new ValidationError($"{source.Login}: no inbox"));
        }

        var unread = inbox["unreadEmails"] is JsonValue u && u.TryGetValue<long>(out var n) ? n : 0;
        var b = new JmapRequestBuilder();
        var q = b.Add("Email/query", new JsonObject
        {
            ["accountId"] = source.AccountId,
            ["filter"] = Filter(inboxId, query),
            ["sort"] = new JsonArray(new JsonObject { ["property"] = "receivedAt", ["isAscending"] = false }),
            ["position"] = position,
            ["limit"] = Math.Max(1, limit),
            ["calculateTotal"] = true,
        });
        var g = b.Add("Email/get", new JsonObject { ["accountId"] = source.AccountId, ["#ids"] = q.Ref("/ids"), ["properties"] = JmapProperties.Json(JmapProperties.EmailList) });
        var t = b.Add("Thread/get", new JsonObject { ["accountId"] = source.AccountId, ["#ids"] = g.Ref("/list/*/threadId") });
        var sent = await source.Client.SendAsync(b.Build(), ct).ConfigureAwait(false);
        if (!sent.IsOk)
        {
            return Result<UnifiedSourcePage>.Fail(sent.Error!);
        }

        var r = sent.Value!;
        var (qa, ga, ta) = (r.Args(q.CallId, q.Method), r.Args(g.CallId, g.Method), r.Args(t.CallId, t.Method));
        if (!qa.IsOk || !ga.IsOk || !ta.IsOk)
        {
            return Result<UnifiedSourcePage>.Fail((qa.Error ?? ga.Error ?? ta.Error)!);
        }

        var sizes = ListPageFetcher.ThreadSizes(ta.Value!.Arr("list") ?? []);
        var rows = ListPageFetcher.Rows(ga.Value!.Arr("list") ?? [], sizes).Select(e => Tag(e, source)).ToList();
        var threads = sizes.ToDictionary(kv => UnifiedRows.RowId(source.Login, source.SharedAccount, kv.Key), kv => kv.Value, StringComparer.Ordinal);
        var total = qa.Value!["total"] is JsonValue tv && tv.TryGetValue<int>(out var tot) ? tot : rows.Count;
        return Result<UnifiedSourcePage>.Ok(new UnifiedSourcePage(source.Key, rows, total, threads, unread));
    }

    /// <summary>A row of <paramref name="source"/> with its unified row id and thread id.</summary>
    public static EmailSummary Tag(EmailSummary e, UnifiedSource source)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(source);
        return e with
        {
            Id = UnifiedRows.RowId(source.Login, source.SharedAccount, e.Id),
            ThreadId = e.ThreadId is { } t ? UnifiedRows.RowId(source.Login, source.SharedAccount, t) : null,
        };
    }

    private static JsonObject Filter(string inboxId, ListQuery query)
    {
        var conditions = new JsonArray(new JsonObject { ["inMailbox"] = inboxId });
        if (query.SearchText is { Length: > 0 } text)
        {
            conditions.Add((JsonNode)new JsonObject { ["text"] = text });
        }

        if (query.HasKeyword is { Length: > 0 } has)
        {
            conditions.Add((JsonNode)new JsonObject { ["hasKeyword"] = has });
        }

        if (query.NotKeyword is { Length: > 0 } not)
        {
            conditions.Add((JsonNode)new JsonObject { ["notKeyword"] = not });
        }

        return conditions.Count == 1 ? (JsonObject)conditions[0]!.DeepClone() : new JsonObject { ["operator"] = "AND", ["conditions"] = conditions };
    }
}
