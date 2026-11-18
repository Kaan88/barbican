// Mirrors: Rust cal_extra.rs:33 fetch_people (ContactCard/query + get, read only); JMAP Contacts (RFC 9610, urn:ietf:params:jmap:contacts) ContactCard/query and /get. Contacts proper are M5; without the capability the list is empty.

using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Requests;

namespace Barbican.Core.Calendar.Editing;

/// <summary>The default <see cref="IContactCardReader"/>.</summary>
public sealed class ContactCardReader(IAccountManager accounts) : IContactCardReader
{
    private const string ContactsCapability = "urn:ietf:params:jmap:contacts";

    public async Task<Result<IReadOnlyList<JsonNode>>> CardsAsync(string accountKey, CancellationToken ct)
    {
        if (accounts.SessionFor(accountKey)?.Client is not { } client || !client.Session.Has(ContactsCapability))
        {
            return Result<IReadOnlyList<JsonNode>>.Ok([]);
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
}
