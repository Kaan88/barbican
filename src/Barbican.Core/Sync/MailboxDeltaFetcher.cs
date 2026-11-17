// Mirrors: Rust jmap.rs:440 mailbox_delta (Mailbox/changes up to 20 rounds while hasMore; created fetched whole; updated fetched with
// only updatedProperties + id when every one is a MAILBOX_PROPERTIES member and the folder is cached, else whole; merged with
// apply_object_delta), :390 changes; RFC 8620 §5.2 (/changes), RFC 8621 §2.2 (Mailbox/changes updatedProperties). Owner jmap.

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Requests;

namespace Barbican.Core.Sync;

/// <summary>Brings a folder list up to date with Mailbox/changes.</summary>
public static class MailboxDeltaFetcher
{
    private const int MaxRounds = 20;

    /// <summary>The merged list and its new state; Ok(null) when no delta is possible from <paramref name="since"/> (re-read in full).</summary>
    public static async Task<Result<(IReadOnlyList<JsonObject> List, string State)?>> FetchAsync(
        IJmapClient client, string since, IReadOnlyList<JsonObject> have, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(have);
        var acc = client.Session.MailAccountId;
        List<string> created = [], updated = [], destroyed = [];
        List<string>? props = [];
        var state = since;
        for (var round = 0; round < MaxRounds; round++)
        {
            var r = await client.SendAsync(JmapRequest.Of(JmapRequest.Call("Mailbox/changes", new JsonObject { ["accountId"] = acc, ["sinceState"] = state }, "c")), ct)
                .ConfigureAwait(false);
            if (!r.IsOk)
            {
                return Result<(IReadOnlyList<JsonObject>, string)?>.Fail(r.Error!);
            }

            if (r.Value!.MethodResponses.Count == 0 || ChangesParser.Parse(r.Value.MethodResponses[0], state, client.Session.IsStalwart) is not { } ch)
            {
                return Result<(IReadOnlyList<JsonObject>, string)?>.Ok(null);
            }

            created.AddRange(ch.Created);
            updated.AddRange(ch.Updated);
            destroyed.AddRange(ch.Destroyed);
            props = props is not null && ch.UpdatedProperties is { } p ? [.. props, .. p] : null;
            state = ch.NewState;
            if (!ch.HasMore)
            {
                var list = await MergeAsync(client, have, created, updated, destroyed, props, ct).ConfigureAwait(false);
                return list.Map(l => ((IReadOnlyList<JsonObject>, string)?)(l, state));
            }
        }

        return Result<(IReadOnlyList<JsonObject>, string)?>.Ok(null);
    }

    private static async Task<Result<IReadOnlyList<JsonObject>>> MergeAsync(
        IJmapClient client, IReadOnlyList<JsonObject> have, List<string> created, List<string> updated, List<string> destroyed, List<string>? props,
        CancellationToken ct)
    {
        var acc = client.Session.MailAccountId;
        var gone = destroyed.ToHashSet(StringComparer.Ordinal);
        var partial = props is { Count: > 0 } && props.TrueForAll(x => JmapProperties.Mailbox.Contains(x, StringComparer.Ordinal));
        var upd = updated.Where(i => !gone.Contains(i) && !created.Contains(i, StringComparer.Ordinal)).Distinct(StringComparer.Ordinal).ToList();

        // a counts-only fetch merges into a cached folder; one the cache lacks is fetched whole, or it would join with no name or role
        var (fetchUpd, missing) = ObjectDelta.SplitKnown(upd, have, partial);
        var fetchFull = created.Where(i => !gone.Contains(i)).Concat(missing).ToList();
        var full = await client.GetAllAsync("Mailbox/get", acc, fetchFull, JmapProperties.Json(JmapProperties.Mailbox), ct).ConfigureAwait(false);
        if (!full.IsOk)
        {
            return full;
        }

        var updProps = partial ? JmapProperties.Json([.. props!, "id"]) : JmapProperties.Json(JmapProperties.Mailbox);
        var part = await client.GetAllAsync("Mailbox/get", acc, fetchUpd, updProps, ct).ConfigureAwait(false);
        if (!part.IsOk)
        {
            return part;
        }

        return Result<IReadOnlyList<JsonObject>>.Ok(ObjectDelta.Apply(have, destroyed, [.. full.Value!, .. part.Value!]));
    }
}
