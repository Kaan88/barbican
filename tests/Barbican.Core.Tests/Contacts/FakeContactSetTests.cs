// Green: the FakeJmap contact set arm (M5 plan section 4.13): ContactCard/set create with uid and addressBookIds, update with pointer patches and Stalwart's invalidPatch for a pointer into a missing map (the keywords/x case, B-CON-006), destroy, notFound, invalidProperties, ifInState, the request log and FailNextContactSet;
// AddressBook/set create, rename, onSuccessSetIsDefault and onDestroyRemoveContents. The seed has the shapes the stream's tests read.

#pragma warning disable MA0006, MA0002 // assertions compare string literals

using System.Text.Json.Nodes;
using Barbican.Core.Jmap;
using Barbican.FakeJmap;

namespace Barbican.Core.Tests.Contacts;

public sealed class FakeContactSetTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<JsonObject> CallAsync(FakeJmapServer server, string method, JsonObject args)
    {
        var client = await FakeJmapHarness.ConnectAsync(server, TimeProvider.System, _ => { }, false, Ct);
        args["accountId"] = client.Session.MailAccountId;
        var r = await client.SendAsync(JmapRequest.Of(JmapRequest.Call(method, args, "c")), Ct);
        return r.Value!.MethodResponses[0][1]!.AsObject();
    }

    private static (FakeJmapServer Server, FakeContacts Contacts) Server(int bulk = 0)
    {
        var contacts = FakeSeed.ContactsM5(bulk);
        return (new FakeJmapServer(new FakeJmapOptions { ContactsCapability = true, Contacts = contacts }), contacts);
    }

    [Fact]
    public async Task A_card_is_created_updated_and_destroyed_and_the_change_log_says_so()
    {
        var (server, contacts) = Server();
        var since = contacts.State("ContactCard");
        var created = await CallAsync(server, "ContactCard/set", new JsonObject { ["create"] = new JsonObject { ["c1"] = new JsonObject { ["name"] = new JsonObject { ["full"] = "New One" }, ["addressBookIds"] = new JsonObject { ["ab1"] = true } } } });
        var id = (string)created["created"]!["c1"]!["id"]!;
        Assert.StartsWith("urn:uuid:", (string?)created["created"]!["c1"]!["uid"], StringComparison.Ordinal);
        Assert.Contains(contacts.Cards(), c => (string?)c["id"] == id);

        var updated = await CallAsync(server, "ContactCard/set", new JsonObject { ["update"] = new JsonObject { [id] = new JsonObject { ["name/full"] = "Renamed", ["keywords"] = new JsonObject { ["VIP"] = true } } } });
        Assert.NotNull(updated["updated"]);
        Assert.Equal("Renamed", (string?)contacts.Cards().First(c => (string?)c["id"] == id)["name"]!["full"]);

        var destroyed = await CallAsync(server, "ContactCard/set", new JsonObject { ["destroy"] = new JsonArray(id, "ghost") });
        Assert.Equal(id, (string?)destroyed["destroyed"]![0]);
        Assert.Equal("notFound", (string?)destroyed["notDestroyed"]!["ghost"]!["type"]);
        var changes = contacts.ChangesSince("ContactCard", since)!.Value;
        Assert.Contains(id, changes.Created);
        Assert.Contains(id, changes.Destroyed);
    }

    [Fact]
    public async Task A_pointer_into_a_map_the_card_lacks_is_an_invalid_patch_and_a_missing_book_is_invalid_properties()
    {
        var (server, _) = Server();
        var r = await CallAsync(server, "ContactCard/set", new JsonObject { ["update"] = new JsonObject { [FakeSeed.ContactBabbageId] = new JsonObject { ["keywords/VIP"] = true } } });
        Assert.Equal("invalidPatch", (string?)r["notUpdated"]![FakeSeed.ContactBabbageId]!["type"]);
        var fine = await CallAsync(server, "ContactCard/set", new JsonObject { ["update"] = new JsonObject { [FakeSeed.ContactAdaId] = new JsonObject { ["keywords/Friend"] = true } } });
        Assert.NotNull(fine["updated"]);
        var bad = await CallAsync(server, "ContactCard/set", new JsonObject { ["create"] = new JsonObject { ["c"] = new JsonObject { ["name"] = new JsonObject { ["full"] = "X" }, ["addressBookIds"] = new JsonObject { ["nope"] = true } } } });
        Assert.Equal("invalidProperties", (string?)bad["notCreated"]!["c"]!["type"]);
    }

    [Fact]
    public async Task If_in_state_a_failed_write_and_the_request_log()
    {
        var (server, contacts) = Server();
        var stale = await CallAsync(server, "ContactCard/set", new JsonObject { ["ifInState"] = "ContactCard-0" });
        Assert.Equal("stateMismatch", (string?)stale["type"]);
        var api = server.Api;
        api.FailNextContactSet("forbidden");
        var failed = await CallAsync(server, "ContactCard/set", new JsonObject { ["update"] = new JsonObject { [FakeSeed.ContactAdaId] = new JsonObject { ["name/full"] = "Nope" } } });
        Assert.Equal("forbidden", (string?)failed["notUpdated"]![FakeSeed.ContactAdaId]!["type"]);
        Assert.Equal("Ada Lovelace", (string?)contacts.Cards().First(c => (string?)c["id"] == FakeSeed.ContactAdaId)["name"]!["full"]);
        Assert.Equal(2, api.ContactSetRequests.Count);
    }

    [Fact]
    public async Task A_book_is_created_made_default_and_removed_with_its_cards_only_when_asked()
    {
        var (server, contacts) = Server();
        var made = await CallAsync(server, "AddressBook/set", new JsonObject { ["create"] = new JsonObject { ["n"] = new JsonObject { ["name"] = "Club" } }, ["onSuccessSetIsDefault"] = "#n" });
        var id = (string)made["created"]!["n"]!["id"]!;
        Assert.Equal(id, (string?)contacts.Books().Single(b => (bool?)b["isDefault"] == true)["id"]);
        var refused = await CallAsync(server, "AddressBook/set", new JsonObject { ["destroy"] = new JsonArray("ab2") });
        Assert.Equal("addressBookHasContents", (string?)refused["notDestroyed"]!["ab2"]!["type"]);
        var removed = await CallAsync(server, "AddressBook/set", new JsonObject { ["destroy"] = new JsonArray("ab2"), ["onDestroyRemoveContents"] = true });
        Assert.Equal("ab2", (string?)removed["destroyed"]![0]);
        Assert.DoesNotContain(contacts.Cards(), c => (c["addressBookIds"] as JsonObject)?.ContainsKey("ab2") == true);
    }

    [Fact]
    public async Task The_seed_has_two_books_a_group_a_duplicate_pair_a_photo_and_enough_cards_to_chunk()
    {
        var (server, contacts) = Server(600);
        Assert.Equal(2, contacts.Books().Count);
        Assert.True(contacts.Cards().Count > 600);
        Assert.Contains(contacts.Cards(), c => (string?)c["kind"] == "group");
        var tooMany = await CallAsync(server, "ContactCard/get", new JsonObject { ["ids"] = new JsonArray([.. contacts.Cards().Select(c => (JsonNode)(string)c["id"]!)]) });
        Assert.Equal("requestTooLarge", (string?)tooMany["type"]);
    }
}
