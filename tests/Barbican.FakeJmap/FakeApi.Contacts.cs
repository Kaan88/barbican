// The fake server's contact reads (JMAP Contacts RFC 9610; Stalwart crates/jmap/src/addressbook/get.rs, contact/{get,query}.rs, changes through the generic engine): AddressBook/get, AddressBook/changes, ContactCard/query (filters inAddressBook, uid, kind, text, email; paged with position and limit capped by
// QueryLimitCap), ContactCard/get (properties; the maxObjectsInGet limit answers requestTooLarge as Stalwart does), ContactCard/changes. The set methods are con-data's (FakeApi.ContactsSet.cs). Answers only with FakeJmapOptions.Contacts and ContactsCapability. M5 plan section 4.13. Owner con-data.

using System.Text.Json.Nodes;

namespace Barbican.FakeJmap;

public sealed partial class FakeApi
{
    private (string? Name, JsonObject Args) ContactMethod(string name, JsonObject args) => name switch
    {
        "AddressBook/get" => ContactGet("AddressBook", args),
        "AddressBook/changes" => ContactChanges("AddressBook", args),
        "AddressBook/set" => ContactSet("AddressBook", args),
        "ContactCard/get" => ContactGet("ContactCard", args),
        "ContactCard/changes" => ContactChanges("ContactCard", args),
        "ContactCard/query" => ContactCardQuery(args),
        "ContactCard/set" => ContactSet("ContactCard", args),
        _ => Error("unknownMethod"),
    };

    /// <summary>The contact data an account id names: a shared account's (<see cref="FakeJmapOptions.SharedContacts"/>) or the user's own.</summary>
    private FakeContacts? ContactsOf(JsonObject args) => (string?)args["accountId"] is { } id && options.SharedContacts.TryGetValue(id, out var shared) ? shared : options.Contacts;

    private string ContactAccount(JsonObject args) => (string?)args["accountId"] is { } id && options.SharedContacts.ContainsKey(id) ? id : options.AccountId;

    private (string? Name, JsonObject Args) ContactGet(string type, JsonObject args)
    {
        if (ContactsOf(args) is not { } contacts || !options.ContactsCapability)
        {
            return Error("unknownMethod");
        }

        if (contacts.Fails)
        {
            return Error("serverFail");
        }

        var all = string.Equals(type, "AddressBook", StringComparison.Ordinal) ? contacts.Books() : contacts.Cards();
        var ids = args["ids"] is JsonArray a ? a.Select(x => (string?)x).OfType<string>().ToList() : null;
        if (ids is not null && ids.Count > options.MaxObjectsInGet)
        {
            return Error("requestTooLarge");
        }

        var list = new JsonArray();
        var notFound = new JsonArray();
        foreach (var o in ids is null ? all : ids.Select(i => all.FirstOrDefault(x => string.Equals((string?)x["id"], i, StringComparison.Ordinal))))
        {
            if (o is not null)
            {
                list.Add(Pick(o, args["properties"] as JsonArray));
            }
        }

        if (ids is not null)
        {
            foreach (var i in ids.Where(i => !all.Any(x => string.Equals((string?)x["id"], i, StringComparison.Ordinal))))
            {
                notFound.Add(i);
            }
        }

        return (null, new JsonObject { ["accountId"] = ContactAccount(args), ["state"] = contacts.State(type), ["list"] = list, ["notFound"] = notFound });
    }

    private (string? Name, JsonObject Args) ContactChanges(string type, JsonObject args)
    {
        if (ContactsOf(args) is not { } contacts || !options.ContactsCapability)
        {
            return Error("unknownMethod");
        }

        if (contacts.Fails)
        {
            return Error("serverFail");
        }

        var since = (string?)args["sinceState"] ?? "";
        if (contacts.ChangesSince(type, since) is not { } c)
        {
            return Error("cannotCalculateChanges");
        }

        return (null, new JsonObject
        {
            ["accountId"] = ContactAccount(args),
            ["oldState"] = since,
            ["newState"] = contacts.State(type),
            ["hasMoreChanges"] = false,
            ["created"] = new JsonArray([.. c.Created.Select(i => (JsonNode)i)]),
            ["updated"] = new JsonArray([.. c.Updated.Select(i => (JsonNode)i)]),
            ["destroyed"] = new JsonArray([.. c.Destroyed.Select(i => (JsonNode)i)]),
        });
    }

    private (string? Name, JsonObject Args) ContactCardQuery(JsonObject args)
    {
        if (ContactsOf(args) is not { } contacts || !options.ContactsCapability)
        {
            return Error("unknownMethod");
        }

        if (contacts.Fails)
        {
            return Error("serverFail");
        }

        var filter = args["filter"] as JsonObject;
        var book = (string?)filter?["inAddressBook"];
        var uid = (string?)filter?["uid"];
        var kind = (string?)filter?["kind"];
        var text = ((string?)filter?["text"])?.ToLowerInvariant();
        var email = ((string?)filter?["email"])?.ToLowerInvariant();
        var ids = contacts.Cards()
            .Where(c => book is null || (c["addressBookIds"] as JsonObject)?.ContainsKey(book) == true)
            .Where(c => uid is null || string.Equals((string?)c["uid"], uid, StringComparison.Ordinal))
            .Where(c => kind is null || string.Equals((string?)c["kind"] ?? "individual", kind, StringComparison.Ordinal))
            .Where(c => text is null || c.ToJsonString().Contains(text, StringComparison.OrdinalIgnoreCase))
            .Where(c => email is null || (c["emails"] as JsonObject ?? []).Any(e => ((string?)e.Value?["address"])?.Contains(email, StringComparison.OrdinalIgnoreCase) == true))
            .Select(c => (string)c["id"]!)
            .ToList();
        var position = args["position"] is JsonValue p ? p.GetValue<int>() : 0;
        var limit = Math.Min(args["limit"] is JsonValue l ? l.GetValue<int>() : options.QueryLimitCap, options.QueryLimitCap);
        return (null, new JsonObject
        {
            ["accountId"] = ContactAccount(args),
            ["queryState"] = contacts.State("ContactCard"),
            ["canCalculateChanges"] = false,
            ["position"] = position,
            ["total"] = ids.Count,
            ["ids"] = new JsonArray([.. ids.Skip(position).Take(limit).Select(i => (JsonNode)i)]),
        });
    }
}
