// Mirrors: Rust jmap.rs:592 list_delta (one request of 7 calls: Email/changes, Email/queryChanges, Email/get of /added/*/id and of
// /updated, Thread/changes, Thread/get of /updated and of /created; no delta on any method error, hasMore, a missing newQueryState or a
// Stalwart state that went back; maxChanges = min(256, maxObjectsInGet); needs maxCallsInRequest >= 7); RFC 8620 §5.2 (/changes),
// §5.6 (/queryChanges: cannotCalculateChanges, tooManyChanges), §3.7 (result references). Owner jmap.

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Jmap.Requests;
using Barbican.Core.Jmap.State;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Lists;
using Barbican.Core.Mail.Service;

namespace Barbican.Core.Sync;

/// <summary>What changed in a list since its states, in one request.</summary>
public static class ListDeltaFetcher
{
    private const int MaxChanges = 256;

    /// <summary>The delta, or Ok(null) when none can be calculated (the caller re-reads the list).</summary>
    public static async Task<Result<ListDelta?>> FetchAsync(IJmapClient client, JsonObject filter, ListStates since, CancellationToken ct, IReadOnlyList<SortComparator>? sort = null, bool? collapseThreads = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(since);
        var limits = client.Session.Limits;
        if (limits.MaxCallsInRequest < 7)
        {
            return Result<ListDelta?>.Ok(null);
        }

        var resolvedSort = await ListSortResolver.ResolveAsync(client, sort, ct).ConfigureAwait(false);
        var (request, calls) = Build(client.Session.MailAccountId, filter, since, Math.Min(MaxChanges, limits.MaxObjectsInGet), resolvedSort, collapseThreads);
        var sent = await client.SendAsync(request, ct).ConfigureAwait(false);
        if (!sent.IsOk)
        {
            return Result<ListDelta?>.Fail(sent.Error!);
        }

        return Result<ListDelta?>.Ok(Read(sent.Value!, calls, since, client.Session.IsStalwart));
    }

    /// <summary>The 7-call request (public for the trace and the request-shape tests).</summary>
    public static (JmapRequest Request, CallHandle[] Calls) Build(string acc, JsonObject filter, ListStates since, int maxChanges, IReadOnlyList<SortComparator>? sort = null, bool? collapseThreads = null)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(since);
        var props = JmapProperties.Json(JmapProperties.EmailList);
        var b = new JmapRequestBuilder();
        var c = b.Add("Email/changes", new JsonObject { ["accountId"] = acc, ["sinceState"] = since.Email, ["maxChanges"] = maxChanges });
        var query = new JsonObject
        {
            ["accountId"] = acc,
            ["filter"] = filter.DeepClone(),
            ["sort"] = MessageListOrder.ComparatorsJson(sort),
            ["sinceQueryState"] = since.Query,
            ["calculateTotal"] = true,
        };
        if (collapseThreads is { } collapse) query["collapseThreads"] = collapse;
        var q = b.Add("Email/queryChanges", query);
        var a = b.Add("Email/get", new JsonObject { ["accountId"] = acc, ["#ids"] = q.Ref("/added/*/id"), ["properties"] = props });
        var u = b.Add("Email/get", new JsonObject { ["accountId"] = acc, ["#ids"] = c.Ref("/updated"), ["properties"] = props.DeepClone() });
        var tc = b.Add("Thread/changes", new JsonObject { ["accountId"] = acc, ["sinceState"] = since.Thread, ["maxChanges"] = maxChanges });
        var tu = b.Add("Thread/get", new JsonObject { ["accountId"] = acc, ["#ids"] = tc.Ref("/updated") });
        var tn = b.Add("Thread/get", new JsonObject { ["accountId"] = acc, ["#ids"] = tc.Ref("/created") });
        return (b.Build(), [c, q, a, u, tc, tu, tn]);
    }

    private static ListDelta? Read(JmapResponse r, CallHandle[] calls, ListStates since, bool stalwart)
    {
        var responses = calls.Select(h => r.ByCallId(h.CallId)).ToList();
        if (responses.Exists(x => x is null || string.Equals(x[0].AsString(), "error", StringComparison.Ordinal)))
        {
            return null;
        }

        if (ChangesParser.Parse(responses[0]!, since.Email, stalwart) is not { HasMore: false } ec
            || ChangesParser.Parse(responses[4]!, since.Thread, stalwart) is not { HasMore: false } tc)
        {
            return null;
        }

        var qc = responses[1]![1];
        if (qc.Str("newQueryState") is not { } newQueryState || (stalwart && StalwartState.WentBack(since.Query, newQueryState)))
        {
            return null;
        }

        var emails = new Dictionary<string, EmailSummary>(StringComparer.Ordinal);
        foreach (var m in (responses[2]![1].Arr("list") ?? []).Concat(responses[3]![1].Arr("list") ?? []))
        {
            if (ModelReader.Summary(m) is { } e)
            {
                emails[e.Id] = e;
            }
        }

        var threads = ListPageFetcher.ThreadSizes(responses[5]![1].Arr("list") ?? []);
        foreach (var (k, v) in ListPageFetcher.ThreadSizes(responses[6]![1].Arr("list") ?? []))
        {
            threads[k] = v;
        }

        return new ListDelta
        {
            Removed = qc.Strings("removed"),
            Added = [.. (qc.Arr("added") ?? []).Select(x => (Id: x.Str("id"), Index: x is JsonObject o ? o["index"].AsNumber() : null))
                .Where(x => x.Id is not null && x.Index is not null).Select(x => (x.Id!, (int)x.Index!.Value))],
            Emails = emails.ToDictionary(kv => kv.Key, kv => kv.Value.ThreadId is { } t && threads.TryGetValue(t, out var n) ? kv.Value with { ThreadCount = n } : kv.Value, StringComparer.Ordinal),
            Threads = threads.AsReadOnly(),
            DestroyedThreads = tc.Destroyed,
            Total = qc is JsonObject qo && qo["total"].AsNumber() is { } total ? (int)total : null,
            QueryState = newQueryState,
            EmailState = ec.NewState,
            ThreadState = tc.NewState,
        };
    }
}
