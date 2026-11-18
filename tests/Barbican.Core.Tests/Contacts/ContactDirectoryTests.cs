// M5 con-data: the directory (RFC 9670 Principal/query with Principal/get back-referenced, paged; the Principal/get fallback when the server refuses the query; B-CON-025, D-M5-13) against a scripted client, as the fake server has no Principal/get.

#pragma warning disable MA0006, MA0002 // assertions compare string literals

using System.Text.Json.Nodes;
using Barbican.Core.Contacts;
using Barbican.Core.Jmap;
using Barbican.Core.Storage;

namespace Barbican.Core.Tests.Contacts;

public sealed class ContactDirectoryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static JsonObject Principal(int i) => new() { ["id"] = "p" + i, ["type"] = "individual", ["name"] = "Person " + i, ["email"] = "p" + i + "@x.example" };

    private static JmapResponse Respond(params JsonArray[] methods) => new() { MethodResponses = methods };

    [Fact]
    public async Task Principals_are_read_page_by_page_through_a_back_reference()
    {
        var all = Enumerable.Range(0, 5).Select(Principal).ToList();
        var client = new ContactScriptedClient(
            request =>
            {
                var args = request.MethodCalls[0][1]!.AsObject();
                var from = (int)args["position"]!;
                var page = all.Skip(from).Take((int)args["limit"]!).ToList();
                Assert.Equal("0", (string?)request.MethodCalls[1][1]!["#ids"]!["resultOf"]);
                return Respond(
                    ["Principal/query", new JsonObject { ["ids"] = new JsonArray([.. page.Select(p => (JsonNode)(string)p["id"]!)]) }, "0"],
                    ["Principal/get", new JsonObject { ["list"] = new JsonArray([.. page.Select(p => (JsonNode)p.DeepClone())]) }, "1"]);
            },
            [ContactOptions.PrincipalsCapability],
            maxGet: 2);
        var directory = new ContactDirectory(client);
        Assert.True(directory.IsAvailable(ContactScriptedClient.Key));
        var r = await directory.LoadAsync(ContactScriptedClient.Key, Ct);
        Assert.True(r.IsOk);
        Assert.False(r.Value.Fallback);
        Assert.Equal(5, r.Value.Principals.Count);

        // pages of 2, 2, 1 and the last one is short: three requests, the properties asked are the seven of Rust
        Assert.Equal(3, client.Requests.Count);
        Assert.Equal(7, client.Requests[0].MethodCalls[1][1]!["properties"]!.AsArray().Count);
    }

    [Fact]
    public async Task A_server_that_refuses_the_query_still_lists_the_principals_it_knows()
    {
        var client = new ContactScriptedClient(
            request => string.Equals((string?)request.MethodCalls[0][0], "Principal/query", StringComparison.Ordinal)
                ? Respond(["error", new JsonObject { ["type"] = "forbidden" }, "0"])
                : Respond(["Principal/get", new JsonObject { ["list"] = new JsonArray(Principal(1)) }, "0"]),
            [ContactOptions.PrincipalsCapability]);
        var r = await new ContactDirectory(client).LoadAsync(ContactScriptedClient.Key, Ct);
        Assert.True(r.IsOk);
        Assert.True(r.Value.Fallback);
        Assert.Single(r.Value.Principals);
        Assert.True(client.Requests[1].MethodCalls[0][1]!.AsObject().ContainsKey("ids"));

        var broken = new ContactScriptedClient(_ => Respond(["error", new JsonObject { ["type"] = "serverFail" }, "0"]), [ContactOptions.PrincipalsCapability]);
        Assert.False((await new ContactDirectory(broken).LoadAsync(ContactScriptedClient.Key, Ct)).IsOk);
    }

    [Fact]
    public async Task The_directory_needs_the_capability_and_the_setting()
    {
        var without = new ContactScriptedClient(_ => Respond(), [ContactOptions.Capability]);
        var directory = new ContactDirectory(without);
        Assert.False(directory.IsAvailable(ContactScriptedClient.Key));
        Assert.False((await directory.LoadAsync(ContactScriptedClient.Key, Ct)).IsOk);
        Assert.Empty(without.Requests);

        var with = new ContactScriptedClient(_ => Respond(), [ContactOptions.PrincipalsCapability]);
        var off = new PrefsStub(new JsonObject { ["showContactsDirectory"] = false });
        Assert.False(new ContactDirectory(with, off).IsAvailable(ContactScriptedClient.Key));
        Assert.True(new ContactDirectory(with, new PrefsStub([])).IsAvailable(ContactScriptedClient.Key));
        Assert.False(new ContactDirectory(with).IsAvailable("unknown"));
    }

    [Fact]
    public void The_listing_leaves_out_resources_without_an_address_and_sorts_by_name()
    {
        var list = ContactDirectory.Listed(
        [
            new JsonObject { ["name"] = "zed", ["type"] = "individual" },
            new JsonObject { ["name"] = "Room 1", ["type"] = "resource" },
            new JsonObject { ["name"] = "Room 2", ["type"] = "resource", ["email"] = "r2@x.example" },
            new JsonObject { ["name"] = "Anna", ["type"] = "group" },
        ]);
        Assert.Equal(["Anna", "Room 2", "zed"], list.Select(p => (string?)p["name"]).ToArray());
    }

    private sealed class PrefsStub(JsonObject values) : IPrefsStore
    {
        public PrefsSnapshot Current { get; } = PrefsSnapshot.FromJson(values);

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }
    }
}
