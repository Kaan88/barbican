// The fake server's mail methods: RFC 8621 §2.1/§2.3 (Mailbox/get, Mailbox/query), §3.1 (Thread/get), §4.2 (Email/get: properties,
// fetchTextBodyValues, fetchHTMLBodyValues, maxBodyValueBytes -> isTruncated), §4.4 (Email/query: position, anchor, anchorOffset,
// limit, calculateTotal, queryState = the Email state as Stalwart, canCalculateChanges), §4.5 (Email/queryChanges), §4.6 (Email/set
// update with JSON Pointer patches, RFC 8620 §5.3); RFC 8620 §5.1 (requestTooLarge above maxObjectsInGet). Owner jmap.

using System.Text;
using System.Text.Json.Nodes;
using Barbican.Core.Jmap.State;

namespace Barbican.FakeJmap;

public sealed partial class FakeApi
{
    private (string? Name, JsonObject Args) MailboxGet(JsonObject args)
    {
        var all = store.Mailboxes();
        var ids = Ids(args);
        if (ids is { Count: var n } && n > options.MaxObjectsInGet)
        {
            return Error("requestTooLarge");
        }

        var list = ids is null ? all.Take(options.MaxObjectsInGet) : all.Where(m => ids.Contains((string)m["id"]!, StringComparer.Ordinal));
        return (null, GetResponse(store.State("Mailbox"), list.Select(m => Project(m, args["properties"] as JsonArray)), ids, all.Select(m => (string)m["id"]!)));
    }

    private (string? Name, JsonObject Args) MailboxQuery(JsonObject args)
    {
        var ids = store.Mailboxes().Select(m => (JsonNode)(string)m["id"]!).ToList();
        var position = (int?)args["position"] ?? 0;
        var limit = Math.Min((int?)args["limit"] ?? ids.Count, options.QueryLimitCap);
        return (null, new JsonObject
        {
            ["accountId"] = options.AccountId,
            ["queryState"] = store.State("Mailbox"),
            ["canCalculateChanges"] = false,
            ["position"] = position,
            ["ids"] = new JsonArray([.. ids.Skip(position).Take(limit).Select(x => x!.DeepClone())]),
            ["total"] = ids.Count,
        });
    }

    private (string? Name, JsonObject Args) ThreadGet(JsonObject args)
    {
        var ids = Ids(args) ?? [];
        var found = ids.Select(store.Thread).OfType<JsonObject>().ToList();
        return (null, GetResponse(store.State("Thread"), found, ids, found.Select(t => (string)t["id"]!)));
    }

    private (string? Name, JsonObject Args) EmailGet(JsonObject args)
    {
        var ids = Ids(args) ?? store.EmailIds().Take(options.MaxObjectsInGet).ToList();
        if (ids.Count > options.MaxObjectsInGet)
        {
            return Error("requestTooLarge");
        }

        var max = (long?)args["maxBodyValueBytes"] ?? 0;
        var text = (bool?)args["fetchTextBodyValues"] ?? false;
        var html = (bool?)args["fetchHTMLBodyValues"] ?? false;
        var emails = store.Emails(ids).Select(e => Project(WithBodyValues(e, text, html, max), args["properties"] as JsonArray)).ToList();
        return (null, GetResponse(store.State("Email"), emails, ids, emails.Select(e => (string)e["id"]!)));
    }

    private (string? Name, JsonObject Args) EmailQuery(JsonObject args)
    {
        if (options.RefuseKeywordSort && (args["sort"] as JsonArray ?? []).OfType<JsonObject>().Any(c => string.Equals((string?)c["property"], "hasKeyword", StringComparison.Ordinal)))
        {
            return Error("unsupportedSort");
        }

        var ids = store.Query(args["filter"], args["sort"] as JsonArray);
        if ((bool?)args["collapseThreads"] == true)
        {
            // RFC 8621 4.4.3: only the first (newest) message of each conversation
            var seen = new HashSet<string>(StringComparer.Ordinal);
            ids = [.. ids.Where(id => seen.Add((string?)store.Email(id)?["threadId"] ?? id))];
        }

        var limit = Math.Min((int?)args["limit"] ?? 50, options.QueryLimitCap);
        int position;
        if ((string?)args["anchor"] is { } anchor)
        {
            var at = ids.ToList().IndexOf(anchor);
            if (at < 0)
            {
                return Error("anchorNotFound");
            }

            position = Math.Max(0, at + ((int?)args["anchorOffset"] ?? 0));
        }
        else
        {
            position = (int?)args["position"] ?? 0;
            position = position < 0 ? Math.Max(0, ids.Count + position) : position;
        }

        var o = new JsonObject
        {
            ["accountId"] = options.AccountId,
            ["queryState"] = store.State("Email"),
            ["canCalculateChanges"] = true,
            ["position"] = position,
            ["ids"] = new JsonArray([.. ids.Skip(position).Take(limit).Select(x => (JsonNode)x)]),
        };
        if ((bool?)args["calculateTotal"] == true)
        {
            o["total"] = ids.Count;
        }

        return (null, o);
    }

    private (string? Name, JsonObject Args) EmailQueryChanges(JsonObject args)
    {
        if (TakeFault(FakeFault.CannotCalculateChanges))
        {
            return Error("cannotCalculateChanges");
        }

        if (TakeFault(FakeFault.TooManyChanges))
        {
            return Error("tooManyChanges");
        }

        var since = (string?)args["sinceQueryState"] ?? "";
        if (store.QueryAt(since, args["filter"], args["sort"] as JsonArray) is not { } before)
        {
            return Error("cannotCalculateChanges");
        }

        var now = store.Query(args["filter"], args["sort"] as JsonArray);
        var old = before.ToHashSet(StringComparer.Ordinal);
        var current = now.ToHashSet(StringComparer.Ordinal);
        var moved = !before.Where(current.Contains).SequenceEqual(now.Where(old.Contains), StringComparer.Ordinal);
        var newState = store.State("Email");
        if (TakeFault(FakeFault.QueryStateWentBack))
        {
            newState = StalwartState.Encode(Math.Max(1, (StalwartState.ChangeId(since) ?? 2) - 1));
        }

        var o = new JsonObject
        {
            ["accountId"] = options.AccountId,
            ["oldQueryState"] = since,
            ["newQueryState"] = newState,
            ["removed"] = new JsonArray([.. before.Where(i => moved || !current.Contains(i)).Select(x => (JsonNode)x)]),
            ["added"] = new JsonArray([.. now.Select((id, index) => (id, index)).Where(x => moved || !old.Contains(x.id))
                .Select(x => (JsonNode)new JsonObject { ["id"] = x.id, ["index"] = x.index })]),
        };
        if ((bool?)args["calculateTotal"] == true)
        {
            o["total"] = now.Count;
        }

        return (null, o);
    }

    private JsonObject GetResponse(string state, IEnumerable<JsonObject> list, IReadOnlyList<string>? asked, IEnumerable<string> found)
    {
        var have = found.ToHashSet(StringComparer.Ordinal);
        return new JsonObject
        {
            ["accountId"] = options.AccountId,
            ["state"] = state,
            ["list"] = new JsonArray([.. list]),
            ["notFound"] = new JsonArray([.. (asked ?? []).Where(i => !have.Contains(i)).Select(x => (JsonNode)x)]),
        };
    }

    private static List<string>? Ids(JsonObject args) =>
        args["ids"] is JsonArray a ? [.. a.Select(x => (string?)x).OfType<string>()] : null;

    private static JsonObject Project(JsonObject full, JsonArray? properties)
    {
        if (properties is null)
        {
            return full;
        }

        var o = new JsonObject { ["id"] = full["id"]?.DeepClone() };
        foreach (var p in properties.Select(x => (string?)x).OfType<string>())
        {
            if (full.TryGetPropertyValue(p, out var v))
            {
                o[p] = v?.DeepClone();
            }
        }

        return o;
    }

    private static JsonObject WithBodyValues(JsonObject e, bool text, bool html, long max)
    {
        var wanted = new HashSet<string>(StringComparer.Ordinal);
        if (text)
        {
            wanted.UnionWith((e["textBody"] as JsonArray ?? []).Select(p => (string?)p?["partId"]).OfType<string>());
        }

        if (html)
        {
            wanted.UnionWith((e["htmlBody"] as JsonArray ?? []).Select(p => (string?)p?["partId"]).OfType<string>());
        }

        var values = new JsonObject();
        foreach (var (partId, v) in (JsonObject)e["bodyValues"]!)
        {
            if (wanted.Contains(partId) && v is JsonObject bv)
            {
                values[partId] = Truncate(bv, max);
            }
        }

        e["bodyValues"] = values;
        return e;
    }

    private static JsonObject Truncate(JsonObject bv, long max)
    {
        var value = (string)bv["value"]!;
        if (max <= 0 || Encoding.UTF8.GetByteCount(value) <= max)
        {
            return (JsonObject)bv.DeepClone();
        }

        var cut = value.Length;
        while (Encoding.UTF8.GetByteCount(value.AsSpan(0, cut)) > max)
        {
            cut = (int)(cut * (double)max / Encoding.UTF8.GetByteCount(value.AsSpan(0, cut)));
        }

        if (cut > 0 && char.IsHighSurrogate(value[cut - 1]))
        {
            cut--;
        }

        return new JsonObject { ["value"] = value[..cut], ["isEncodingProblem"] = false, ["isTruncated"] = true };
    }
}
