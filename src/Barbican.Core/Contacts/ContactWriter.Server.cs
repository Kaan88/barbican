// The server side of the writes: which account and id, the book a new card goes to, the optimistic patch of the store with its rollback, and the pointer patch Stalwart accepts.
// Mirrors Rust contacts.rs:default_book_for, calendar.rs:236 server_ref, contacts_x.rs:215 save; Stalwart crates/jmap/src/contact/set.rs (a pointer into a map the card lacks is an invalidPatch).

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Json;

namespace Barbican.Core.Contacts;

public sealed partial class ContactWriter
{
    private JsonNode? StoredCard(string accountKey, string cardId) =>
        store.Snapshot(accountKey)?.Cards.FirstOrDefault(c => string.Equals(c.Str("id"), cardId, StringComparison.Ordinal));

    /// <summary>The account and server id of a card: a shared account's cards carry their own (<c>_acc</c>, <c>_rid</c>), else the namespace of the id, else the primary contacts account.</summary>
    private (string Account, string ServerId) ServerRef(IJmapClient client, string accountKey, string cardId)
    {
        var primary = client.Session.AccountFor(ContactOptions.Capability);
        var stored = StoredCard(accountKey, cardId);
        if (stored?.Str("_rid") is { } rid)
        {
            return (stored.Str("_acc") ?? primary, rid);
        }

        var (account, id) = ContactIds.SplitNs(cardId);
        return (account ?? primary, id);
    }

    private static (string Account, string ServerId) ServerBook(IJmapClient client, string bookId)
    {
        var (account, id) = ContactIds.SplitNs(bookId);
        return (account ?? client.Session.AccountFor(ContactOptions.Capability), id);
    }

    /// <summary>The account and server id of the book a new card goes to: the one asked for, else the default of the login (created once when there is none, B-CON-003).</summary>
    private async Task<Result<(string Account, string BookId)>> BookAsync(IJmapClient client, string accountKey, string? bookId, CancellationToken ct)
    {
        if (bookId is not null)
        {
            return Result<(string, string)>.Ok(ServerBook(client, bookId));
        }

        var made = await books.EnsureDefaultAsync(accountKey, ct).ConfigureAwait(false);
        return made.IsOk ? Result<(string, string)>.Ok((client.Session.AccountFor(ContactOptions.Capability), made.Value!)) : Result<(string, string)>.Fail(made.Error!);
    }

    /// <summary>A card ready to create: no nulls, no id, its type and version, a uid, in one book.</summary>
    internal static JsonObject NewCard(JsonObject source, string serverBook)
    {
        var card = ContactJson.Clean(source);
        _ = card.Remove("id");
        _ = card.Remove("_rid");
        _ = card.Remove("_acc");
        _ = card.Remove("_shared");
        card["@type"] = "Card";
        card["version"] = "1.0";
        if (card.Str("uid") is not { Length: > 0 })
        {
            card["uid"] = ContactImport.NewUid();
        }

        card["addressBookIds"] = new JsonObject { [serverBook] = true };
        return card;
    }

    /// <summary>One card's patch: the store changes at once, the server is asked, and the store is put back when the server refuses; the push delta (or the refresh) confirms.</summary>
    private async Task<Result<Unit>> WritePatchAsync(IJmapClient client, string accountKey, string cardId, JsonObject patch, CancellationToken ct, bool refresh = true)
    {
        var (account, serverId) = ServerRef(client, accountKey, cardId);
        var stored = StoredCard(accountKey, cardId);
        var previous = store.Patch(accountKey, cardId, (JsonObject)patch.DeepClone());
        var result = await ContactJmap.UpdateAsync(client, "ContactCard", account, serverId, ServerPatch(stored, patch), ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            store.Restore(accountKey, cardId, previous);
            return result;
        }

        if (refresh)
        {
            _ = await store.RefreshAsync(accountKey, ct).ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>The patch as Stalwart takes it: a pointer into a map the stored card does not have (keywords/x on a card without keywords, members/x on a group without members) is sent as the whole map.</summary>
    internal static JsonObject ServerPatch(JsonNode? stored, JsonObject patch)
    {
        var output = new JsonObject();
        var whole = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var (path, value) in patch)
        {
            var slash = path.IndexOf('/', StringComparison.Ordinal);
            if (slash > 0 && stored is not null && ContactJson.Get(stored, path[..slash]) is not JsonObject)
            {
                var first = path[..slash];
                if (!whole.TryGetValue(first, out var map))
                {
                    map = [];
                    whole[first] = map;
                }

                Barbican.Core.Calendar.JsonPointerPatch.Apply(map, new JsonObject { [path[(slash + 1)..]] = value?.DeepClone() });
            }
            else
            {
                output[path] = value?.DeepClone();
            }
        }

        foreach (var (k, m) in whole)
        {
            output[k] = m;
        }

        return output;
    }
}
