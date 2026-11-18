// M5 integration: the FakeJmap arms con-data could not add (SharedContacts, the contacts push through PushHub, Principal/get with Principals) and what rides on them: shared address books in the store and the writer, the directory against the fake server, a contact push as the event source sends it.

#pragma warning disable MA0006, MA0002 // assertions compare string literals

using System.Text.Json.Nodes;
using Barbican.Core.Contacts;
using Barbican.Core.Mail.Service;
using Barbican.Core.Sync;
using Barbican.Core.Tests.Jmap;
using Barbican.FakeJmap;
using Microsoft.Extensions.Time.Testing;

namespace Barbican.Core.Tests.Contacts;

public sealed class ContactSharedDirectoryPushTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static FakeContacts SharedBook()
    {
        var shared = new FakeContacts();
        var book = shared.AddBook("Team", true);
        _ = shared.AddCard(book, new JsonObject { ["name"] = new JsonObject { ["full"] = "Shared Sam" }, ["emails"] = new JsonObject { ["e0"] = new JsonObject { ["address"] = "sam@team.example.org" } } });
        return shared;
    }

    private static FakeJmapOptions WithShared(FakeContacts shared) => new() { SharedContacts = new Dictionary<string, FakeContacts>(StringComparer.Ordinal) { ["team"] = shared } };

    [Fact]
    public async Task A_shared_address_book_is_listed_in_the_session_and_loaded_with_namespaced_ids()
    {
        var rig = ContactRig.Create();
        var (key, server, client) = await rig.AddAsync("user@example.org", FakeSeed.ContactsM5(0), WithShared(SharedBook()));
        Assert.True(client.Session.Raw.GetProperty("accounts").TryGetProperty("team", out var account));
        Assert.True(account.GetProperty("accountCapabilities").TryGetProperty("urn:ietf:params:jmap:contacts", out _));

        var store = new ContactStore(rig, rig.Hooks, rig.Time);
        Assert.True((await store.LoadAsync(key, Ct)).IsOk);
        var snapshot = store.Snapshot(key)!;
        var sam = snapshot.Cards.First(c => (string?)c["name"]!["full"] == "Shared Sam");
        Assert.True((bool?)sam["_shared"]);
        Assert.Equal("team", (string?)sam["_acc"]);
        Assert.Contains(snapshot.Books, b => (string?)b["name"] == "Team" && (bool?)b["_shared"] == true);
        Assert.Contains(snapshot.Cards, c => (string?)c["name"]!["full"] == "Ada Lovelace" && (bool?)c["_shared"] != true);
        Assert.True(server.Count("ContactCard/query") >= 2);
    }

    [Fact]
    public async Task A_card_of_a_shared_book_is_written_in_the_shared_account()
    {
        var shared = SharedBook();
        var rig = ContactRig.Create();
        var (key, server, _) = await rig.AddAsync("user@example.org", FakeSeed.ContactsM5(0), WithShared(shared));
        var store = new ContactStore(rig, rig.Hooks, rig.Time);
        _ = await store.LoadAsync(key, Ct);
        var writer = new ContactWriter(rig, store, new AddressBooks(rig, store), rig.Hooks);
        var sam = store.Snapshot(key)!.Cards.First(c => (string?)c["name"]!["full"] == "Shared Sam");

        var r = await writer.UpdateAsync(key, (string)sam["id"]!, new JsonObject { ["notes"] = new JsonObject { ["n0"] = new JsonObject { ["@type"] = "Note", ["note"] = "met at the fair" } } }, Ct);

        Assert.True(r.IsOk);
        Assert.Equal("met at the fair", (string?)shared.Cards()[0]["notes"]!["n0"]!["note"]);
        Assert.Contains(server.Api.ContactSetRequests, q => (string?)q.Args["accountId"] == "team");
    }

    [Fact]
    public async Task The_directory_lists_the_principals_of_the_fake_server_and_leaves_out_resources_without_an_email()
    {
        var principals = new List<JsonObject>
        {
            new() { ["id"] = "p1", ["type"] = "individual", ["name"] = "Zed", ["email"] = "zed@example.org" },
            new() { ["id"] = "p2", ["type"] = "individual", ["name"] = "amy", ["email"] = "amy@example.org" },
            new() { ["id"] = "p3", ["type"] = "resource", ["name"] = "Room 1" },
        };
        var rig = ContactRig.Create();
        var (key, server, _) = await rig.AddAsync("user@example.org", FakeSeed.ContactsM5(0), new FakeJmapOptions { PrincipalsCapability = true, Principals = principals });
        var directory = new ContactDirectory(rig);
        Assert.True(directory.IsAvailable(key));

        var r = await directory.LoadAsync(key, Ct);

        Assert.True(r.IsOk);
        Assert.False(r.Value.Fallback);
        Assert.Equal(3, r.Value.Principals.Count);
        Assert.Equal(["amy", "Zed"], ContactDirectory.Listed(r.Value.Principals).Select(p => (string?)p["name"]).ToArray());
        Assert.Equal(1, server.Count("Principal/query"));
    }

    [Fact]
    public async Task The_directory_pages_through_a_capped_query_and_falls_back_when_the_query_is_forbidden()
    {
        var many = Enumerable.Range(0, 7).Select(i => new JsonObject { ["id"] = "p" + i, ["type"] = "individual", ["name"] = "P" + i, ["email"] = "p" + i + "@example.org" }).ToList();
        var rig = ContactRig.Create();
        var (key, server, _) = await rig.AddAsync("user@example.org", FakeSeed.ContactsM5(0), new FakeJmapOptions { PrincipalsCapability = true, Principals = many, QueryLimitCap = 3, MaxObjectsInGet = 3 });
        var paged = await new ContactDirectory(rig).LoadAsync(key, Ct);
        Assert.True(paged.IsOk);
        Assert.Equal(7, paged.Value.Principals.Count);
        Assert.Equal(3, server.Count("Principal/query"));

        var rig2 = ContactRig.Create();
        var (key2, _, _) = await rig2.AddAsync("user@example.org", FakeSeed.ContactsM5(0), new FakeJmapOptions { PrincipalsCapability = true, Principals = many, PrincipalQueryForbidden = true });
        var fallback = await new ContactDirectory(rig2).LoadAsync(key2, Ct);
        Assert.True(fallback.IsOk);
        Assert.True(fallback.Value.Fallback);
        Assert.Equal(7, fallback.Value.Principals.Count);
    }

    [Fact]
    public async Task A_server_without_principals_has_no_directory()
    {
        var rig = ContactRig.Create();
        var (key, _, _) = await rig.AddAsync("user@example.org", FakeSeed.ContactsM5(0));
        Assert.False(new ContactDirectory(rig).IsAvailable(key));
    }

    [Fact]
    public async Task The_fake_server_pushes_a_contact_change_and_the_store_follows_without_a_hand_made_state_change()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 18, 9, 0, 0, TimeSpan.Zero));
        var contacts = FakeSeed.ContactsM5(0);
        var rig = ContactRig.Create(clock);
        var (key, server, client) = await rig.AddAsync("user@example.org", contacts);
        using var store = new ContactStore(rig, rig.Hooks, clock, rig.Push);
        _ = await store.LoadAsync(key, Ct);
        var raised = new TaskCompletionSource<ContactChangedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        store.Changed += (_, e) => raised.TrySetResult(e);

        using var sync = new MailSync(new MailService(client, clock), rig.Hooks, clock, new PushBackoff(_ => 0), rig.Push);
        using var stop = new CancellationTokenSource();
        var run = sync.RunAsync(stop.Token);
        await Poll.UntilAsync(() => rig.Traced("PUSH open"), "the stream to open");
        server.Push.Ping();
        clock.Advance(MailSync.Coalesce);
        await store.DrainAsync();

        var id = contacts.AddCard("ab1", new JsonObject { ["name"] = new JsonObject { ["full"] = "Pushed by the server" } });
        await Poll.UntilAsync(() => rig.Traced("PUSH state ContactCard"), "the push of the new card");
        clock.Advance(MailSync.Coalesce);
        var e = await raised.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        await store.DrainAsync();
        Assert.Contains(id, e.CardIds);
        await stop.CancelAsync();
        await run;
    }

    [Fact]
    public async Task A_change_in_a_shared_book_is_pushed_under_the_shared_account_id()
    {
        var shared = SharedBook();
        var rig = ContactRig.Create();
        var (_, server, _) = await rig.AddAsync("user@example.org", FakeSeed.ContactsM5(0), WithShared(shared));
        using var stream = server.Push.Open(["ContactCard"]);
        _ = shared.AddCard("ab1", new JsonObject { ["name"] = new JsonObject { ["full"] = "Late joiner" } });

        var buffer = new byte[2048];
        var read = await stream.ReadAsync(buffer, Ct);
        var text = System.Text.Encoding.UTF8.GetString(buffer, 0, read);
        Assert.Contains("event: state", text, StringComparison.Ordinal);
        Assert.Contains("\"team\"", text, StringComparison.Ordinal);
        Assert.Contains("ContactCard", text, StringComparison.Ordinal);
    }
}
