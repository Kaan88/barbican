// Green: the FakeJmap contact arms of the M5 plan (section 4.13) answer AddressBook/get, ContactCard/query (filters and paging), get with the chunk limit, and changes; without options.Contacts they answer unknownMethod; the set arms are in FakeContactSetTests.

using System.Text.Json.Nodes;
using Barbican.Core.Jmap;
using Barbican.FakeJmap;

namespace Barbican.Core.Tests.Contacts;

public sealed class FakeContactArmsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<JsonObject> CallAsync(FakeJmapServer server, string method, JsonObject args)
    {
        var client = await FakeJmapHarness.ConnectAsync(server, TimeProvider.System, _ => { }, false, Ct);
        args["accountId"] = client.Session.MailAccountId;
        var r = await client.SendAsync(JmapRequest.Of(JmapRequest.Call(method, args, "c")), Ct);
        return r.Value!.MethodResponses[0][1]!.AsObject();
    }

    private static (FakeContacts Contacts, string Book, string Ann) Seed()
    {
        var contacts = new FakeContacts();
        var book = contacts.AddBook("Contacts", isDefault: true);
        var ann = contacts.AddCard(book, new JsonObject
        {
            ["name"] = new JsonObject { ["full"] = "Ann Lee" },
            ["emails"] = new JsonObject { ["e0"] = new JsonObject { ["address"] = "ann@x.example" } },
        });
        _ = contacts.AddCard(book, new JsonObject { ["kind"] = "group", ["name"] = new JsonObject { ["full"] = "Friends" }, ["members"] = new JsonObject { ["x"] = true } });
        return (contacts, book, ann);
    }

    [Fact]
    public async Task Books_cards_filters_and_changes_are_served()
    {
        var (contacts, book, ann) = Seed();
        var server = new FakeJmapServer(new FakeJmapOptions { ContactsCapability = true, Contacts = contacts });

        var books = await CallAsync(server, "AddressBook/get", new JsonObject { ["ids"] = null });
        Assert.Equal("Contacts", (string?)books["list"]![0]!["name"]);

        var all = await CallAsync(server, "ContactCard/query", new JsonObject { ["filter"] = new JsonObject { ["inAddressBook"] = book } });
        Assert.Equal(2, all["ids"]!.AsArray().Count);

        var groups = await CallAsync(server, "ContactCard/query", new JsonObject { ["filter"] = new JsonObject { ["kind"] = "group" } });
        Assert.Single(groups["ids"]!.AsArray());

        var byMail = await CallAsync(server, "ContactCard/query", new JsonObject { ["filter"] = new JsonObject { ["email"] = "ANN@x.example" } });
        Assert.Equal(ann, (string?)byMail["ids"]![0]);

        var got = await CallAsync(server, "ContactCard/get", new JsonObject { ["ids"] = new JsonArray(ann, "missing"), ["properties"] = new JsonArray("name") });
        Assert.Equal("Ann Lee", (string?)got["list"]![0]!["name"]!["full"]);
        Assert.Null(got["list"]![0]!["emails"]);
        Assert.Equal("missing", (string?)got["notFound"]![0]);

        var since = (string)got["state"]!;
        _ = contacts.AddCard(book, new JsonObject { ["name"] = new JsonObject { ["full"] = "Bo" } });
        var changes = await CallAsync(server, "ContactCard/changes", new JsonObject { ["sinceState"] = since });
        Assert.Single(changes["created"]!.AsArray());

        contacts.ExpireStates();
        var expired = await CallAsync(server, "ContactCard/changes", new JsonObject { ["sinceState"] = since });
        Assert.Equal("cannotCalculateChanges", (string?)expired["type"]);
    }

    [Fact]
    public async Task Without_the_option_or_the_capability_the_arms_answer_unknown_method()
    {
        var (contacts, _, _) = Seed();
        var plain = await CallAsync(new FakeJmapServer(new FakeJmapOptions()), "AddressBook/get", new JsonObject());
        Assert.Equal("unknownMethod", (string?)plain["type"]);
        var noCapability = await CallAsync(new FakeJmapServer(new FakeJmapOptions { Contacts = contacts }), "ContactCard/query", new JsonObject());
        Assert.Equal("unknownMethod", (string?)noCapability["type"]);
        var set = await CallAsync(new FakeJmapServer(new FakeJmapOptions { ContactsCapability = true }), "ContactCard/set", new JsonObject());
        Assert.Equal("unknownMethod", (string?)set["type"]);
    }
}
