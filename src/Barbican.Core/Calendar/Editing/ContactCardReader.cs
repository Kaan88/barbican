// Mirrors: Rust cal_extra.rs:33 fetch_people (ContactCard/query + get, read only); JMAP Contacts (RFC 9610, urn:ietf:params:jmap:contacts) ContactCard/query and /get. Without the capability the list is empty.
// M5 con-data (D-M5-06): with a contact store the cards are the store's (one source of truth, loaded on first use, the account's own contacts: a shared address book is not a source of birthdays); without one, the M4 read below.

using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Contacts;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Requests;

namespace Barbican.Core.Calendar.Editing;

/// <summary>The default <see cref="IContactCardReader"/>.</summary>
public sealed class ContactCardReader(IAccountManager accounts, IContactStore? store = null) : IContactCardReader
{
    private const string ContactsCapability = "urn:ietf:params:jmap:contacts";

    public async Task<Result<IReadOnlyList<JsonNode>>> CardsAsync(string accountKey, CancellationToken ct)
    {
        if (accounts.SessionFor(accountKey)?.Client is not { } client || !client.Session.Has(ContactsCapability))
        {
            return Result<IReadOnlyList<JsonNode>>.Ok([]);
        }

        if (store is not null)
        {
            return await FromStoreAsync(store, accountKey, ct).ConfigureAwait(false);
        }

        var account = client.Session.AccountFor(ContactsCapability);
        var ids = await client.QueryAllAsync("ContactCard/query", account, [], ct).ConfigureAwait(false);
        if (!ids.IsOk)
        {
            return Result<IReadOnlyList<JsonNode>>.Fail(ids.Error!);
        }

        var cards = new List<JsonNode>();
        foreach (var chunk in ids.Value!.Chunk(Math.Max(1, client.Session.Limits.MaxObjectsInGet)))
        {
            var args = new JsonObject { ["accountId"] = account, ["ids"] = new JsonArray([.. chunk.Select(i => (JsonNode)JsonValue.Create(i)!)]) };
            var r = await client.CallAsync(JmapRequest.Of(JmapRequest.Call("ContactCard/get", args, "g")), ct).ConfigureAwait(false);
            if (!r.IsOk)
            {
                return Result<IReadOnlyList<JsonNode>>.Fail(r.Error!);
            }

            var response = r.Value!.Args("g", "ContactCard/get");
            if (!response.IsOk)
            {
                return Result<IReadOnlyList<JsonNode>>.Fail(response.Error!);
            }

            foreach (var card in response.Value!["list"] as JsonArray ?? [])
            {
                if (card is not null)
                {
                    cards.Add(card.DeepClone());
                }
            }
        }

        return Result<IReadOnlyList<JsonNode>>.Ok(cards);
    }

    private static async Task<Result<IReadOnlyList<JsonNode>>> FromStoreAsync(IContactStore store, string accountKey, CancellationToken ct)
    {
        var snapshot = store.Snapshot(accountKey);
        if (snapshot is null)
        {
            var loaded = await store.LoadAsync(accountKey, ct).ConfigureAwait(false);
            if (!loaded.IsOk)
            {
                return Result<IReadOnlyList<JsonNode>>.Fail(loaded.Error!);
            }

            snapshot = loaded.Value!;
        }

        return Result<IReadOnlyList<JsonNode>>.Ok([.. snapshot.Cards.Where(c => c is not JsonObject o || o["_shared"] is null).Select(c => c.DeepClone())]);
    }
}
