// Mirrors: Bulwark lib/jmap/client.ts:13411-13500 (advertisesArchivedItems, queryArchivedItemIds, restoreArchivedItems, ARCHIVED_ITEM_USING), lib/recovery/archived-items.ts;
// Rust server_settings.rs:137-152 (recover) and :505-512 (restore). Stalwart crates/jmap/src/registry/mapping/archived_item.rs (x:ArchivedItem get, query,
// set with status "requestRestore"). J-074, I-230: the trash recovery link. A request of its own: without an Enterprise licence Stalwart refuses it (forbidden)
// and that must not take other calls down with it. Owner close-b.

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Mail.Reader;

namespace Barbican.Core.Mail.Archive;

/// <summary>Lists and restores the deleted items Stalwart keeps for a while (x:ArchivedItem).</summary>
public static class ArchivedItems
{
    private static readonly string[] Using = [JmapCapabilities.Core, JmapCapabilities.Stalwart];

    /// <summary>True when the account's server advertises <c>urn:stalwart:jmap</c>; the recovery link is hidden otherwise.</summary>
    public static bool Advertised(IJmapClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        return client.Session.IsStalwart;
    }

    /// <summary>The account's archived items, newest first (up to <paramref name="limit"/>). Asks with the accountId filter first and, when the server refuses it, again without.</summary>
    public static async Task<Result<IReadOnlyList<ArchivedItem>>> ListAsync(IJmapClient client, int limit, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(client);
        var account = client.Session.MailAccountId;
        var ids = await QueryAsync(client, account, limit, filtered: true, ct).ConfigureAwait(false);
        if (!ids.IsOk && ids.Error is MethodError { Type: "unsupportedFilter" })
        {
            ids = await QueryAsync(client, account, limit, filtered: false, ct).ConfigureAwait(false);
        }

        if (!ids.IsOk)
        {
            return Result<IReadOnlyList<ArchivedItem>>.Fail(ids.Error!);
        }

        var items = new List<ArchivedItem>();
        var chunk = Math.Max(1, client.Session.Limits.MaxObjectsInGet);
        for (var i = 0; i < ids.Value!.Count; i += chunk)
        {
            var got = await CallAsync(client, "x:ArchivedItem/get", new JsonObject { ["accountId"] = account, ["ids"] = new JsonArray([.. ids.Value.Skip(i).Take(chunk).Select(x => (JsonNode)x)]) }, ct).ConfigureAwait(false);
            if (!got.IsOk)
            {
                return Result<IReadOnlyList<ArchivedItem>>.Fail(got.Error!);
            }

            foreach (var n in (got.Value!["list"] as JsonArray ?? []).OfType<JsonObject>())
            {
                if (n.Str("id") is { } id)
                {
                    items.Add(new ArchivedItem(id, n.Str("subject") ?? string.Empty, DateTimeOffset.TryParse(n.Str("archivedAt"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var at) ? at : null));
                }
            }
        }

        return Result<IReadOnlyList<ArchivedItem>>.Ok([.. items.OrderByDescending(x => x.ArchivedAt)]);
    }

    /// <summary>Asks the server to restore <paramref name="id"/> (status <c>requestRestore</c>); the message returns to where it was.</summary>
    public static async Task<Result<Unit>> RestoreAsync(IJmapClient client, string id, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(id);
        var account = client.Session.MailAccountId;
        var set = await CallAsync(client, "x:ArchivedItem/set", new JsonObject { ["accountId"] = account, ["update"] = new JsonObject { [id] = new JsonObject { ["status"] = "requestRestore" } } }, ct).ConfigureAwait(false);
        if (!set.IsOk)
        {
            return Result<Unit>.Fail(set.Error!);
        }

        return set.Value!["notUpdated"] is JsonObject failed && failed[id] is { } e
            ? Result<Unit>.Fail(SetErrors.From(id, e))
            : Result<Unit>.Ok(default);
    }

    private static async Task<Result<IReadOnlyList<string>>> QueryAsync(IJmapClient client, string account, int limit, bool filtered, CancellationToken ct)
    {
        var args = new JsonObject { ["accountId"] = account, ["limit"] = limit };
        if (filtered)
        {
            args["filter"] = new JsonObject { ["accountId"] = account };
        }

        var q = await CallAsync(client, "x:ArchivedItem/query", args, ct).ConfigureAwait(false);
        return q.IsOk ? Result<IReadOnlyList<string>>.Ok(ReaderCalls.Ids(q.Value!)) : Result<IReadOnlyList<string>>.Fail(q.Error!);
    }

    private static async Task<Result<JsonObject>> CallAsync(IJmapClient client, string method, JsonObject args, CancellationToken ct)
    {
        var sent = await client.SendAsync(JmapRequest.Of(JmapRequest.Call(method, args, "r")) with { Using = Using }, ct).ConfigureAwait(false);
        return sent.IsOk ? sent.Value!.Args("r", method) : Result<JsonObject>.Fail(sent.Error!);
    }
}
