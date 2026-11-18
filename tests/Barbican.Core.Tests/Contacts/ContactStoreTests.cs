// M5 con-data acceptance tests of the contact store over FakeJmap: load, chunking, the optimistic patch and its restore, /changes and the full-load fallback, the push relay and the engine (M5 plan 6.1 step 2; J-071, G-044).

#pragma warning disable MA0006, MA0002 // assertions compare string literals

using System.Text.Json.Nodes;
using Barbican.Core.Contacts;
using Barbican.Core.Mail.Service;
using Barbican.Core.Sync;
using Barbican.Core.Tests.Jmap;
using Barbican.FakeJmap;
using Microsoft.Extensions.Time.Testing;

namespace Barbican.Core.Tests.Contacts;

public sealed class ContactStoreTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static FakeJmapOptions Paged(int cap) => new() { MaxObjectsInGet = cap, QueryLimitCap = cap };

    [Fact]
    public async Task Load_reads_books_and_every_card_in_chunks_with_the_state_strings()
    {
        var rig = ContactRig.Create();
        var (key, server, _) = await rig.AddAsync("user@example.org", FakeSeed.ContactsM5(), Paged(500));
        var store = new ContactStore(rig, rig.Hooks, rig.Time);
        Assert.Null(store.Snapshot(key));
        var r = await store.LoadAsync(key, Ct);
        Assert.True(r.IsOk);
        var s = store.Snapshot(key)!;
        Assert.Equal(["Contacts", "Work"], s.Books.Select(b => (string?)b["name"]).ToArray());
        Assert.Equal(606, s.Cards.Count);
        Assert.StartsWith("AddressBook-", s.BookState, StringComparison.Ordinal);
        Assert.StartsWith("ContactCard-", s.CardState, StringComparison.Ordinal);
        Assert.Equal(FakeSeed.ContactAdaId, (string?)s.Cards.First(c => (string?)c["id"] == FakeSeed.ContactAdaId)["_rid"]);

        // the state read, two chunk gets (500 and 106), two pages of ids (the fake caps a page at 500): never a get over the limit, which the fake answers requestTooLarge
        Assert.Equal(3, server.Count("ContactCard/get"));
        Assert.Equal(2, server.Count("ContactCard/query"));
        Assert.NotNull(LoadedAtOf(store, key));
    }

    private static DateTimeOffset? LoadedAtOf(ContactStore store, string key) => store.LoadedAt(key);

    [Fact]
    public async Task Without_the_capability_the_snapshot_is_empty_and_a_failing_primary_account_is_the_result()
    {
        var rig = ContactRig.Create();
        var (plain, _, _) = await rig.AddAsync("plain@example.org", null);
        var store = new ContactStore(rig, rig.Hooks, rig.Time);
        var r = await store.LoadAsync(plain, Ct);
        Assert.True(r.IsOk);
        Assert.Empty(r.Value!.Cards);
        Assert.Null(store.Snapshot(plain));
        Assert.False((await store.LoadAsync("unknown", Ct)).IsOk);

        var broken = FakeSeed.ContactsM5(0);
        broken.Fails = true;
        var (key, _, _) = await rig.AddAsync("broken@example.org", broken);
        Assert.False((await store.LoadAsync(key, Ct)).IsOk);
    }

    [Fact]
    public async Task Patch_changes_a_copy_at_once_and_restore_puts_the_card_back()
    {
        var rig = ContactRig.Create();
        var (key, _, _) = await rig.AddAsync("user@example.org", FakeSeed.ContactsM5(0));
        var store = new ContactStore(rig, rig.Hooks, rig.Time);
        _ = await store.LoadAsync(key, Ct);
        var held = store.Snapshot(key)!;
        var raised = new List<ContactChangedEventArgs>();
        store.Changed += (_, e) => raised.Add(e);

        var previous = store.Patch(key, FakeSeed.ContactBabbageId, new JsonObject { ["keywords"] = new JsonObject { ["Club"] = true }, ["name/full"] = "C. Babbage" });
        Assert.Null(previous!["keywords"]);
        var now = store.Snapshot(key)!.Cards.First(c => (string?)c["id"] == FakeSeed.ContactBabbageId);
        Assert.Equal("C. Babbage", (string?)now["name"]!["full"]);
        Assert.True((bool?)now["keywords"]!["Club"]);
        Assert.Equal("Charles Babbage", (string?)held.Cards.First(c => (string?)c["id"] == FakeSeed.ContactBabbageId)["name"]!["full"]);

        store.Restore(key, FakeSeed.ContactBabbageId, previous);
        Assert.Equal("Charles Babbage", (string?)store.Snapshot(key)!.Cards.First(c => (string?)c["id"] == FakeSeed.ContactBabbageId)["name"]!["full"]);
        Assert.Equal(2, raised.Count);
        Assert.Null(store.Patch(key, "ghost", new JsonObject { ["a"] = 1 }));
    }

    [Fact]
    public async Task Refresh_applies_the_changes_of_cards_and_books_and_falls_back_to_a_full_load()
    {
        var contacts = FakeSeed.ContactsM5(0);
        var rig = ContactRig.Create();
        var (key, server, _) = await rig.AddAsync("user@example.org", contacts);
        var store = new ContactStore(rig, rig.Hooks, rig.Time);
        _ = await store.LoadAsync(key, Ct);
        var raised = new List<ContactChangedEventArgs>();
        store.Changed += (_, e) => raised.Add(e);

        var added = contacts.AddCard("ab1", new JsonObject { ["name"] = new JsonObject { ["full"] = "Newcomer" } });
        _ = contacts.Update("ContactCard", FakeSeed.ContactAdaId, c => c["name"]!["full"] = "Ada King");
        _ = contacts.Destroy("ContactCard", FakeSeed.ContactBabbageId);
        var book = contacts.AddBook("Club");
        Assert.True((await store.RefreshAsync(key, Ct)).IsOk);
        var s = store.Snapshot(key)!;
        Assert.Contains(s.Cards, c => (string?)c["id"] == added);
        Assert.DoesNotContain(s.Cards, c => (string?)c["id"] == FakeSeed.ContactBabbageId);
        Assert.Equal("Ada King", (string?)s.Cards.First(c => (string?)c["id"] == FakeSeed.ContactAdaId)["name"]!["full"]);
        Assert.Contains(s.Books, b => (string?)b["id"] == book);
        var e = Assert.Single(raised);
        Assert.True(e.BooksChanged);
        Assert.Contains(added, e.CardIds);
        Assert.Contains(FakeSeed.ContactBabbageId, e.CardIds);
        Assert.Equal(0, server.Count("ContactCard/query") - 1);

        // the server forgot every state: one full load again
        contacts.ExpireStates();
        _ = contacts.AddCard("ab1", new JsonObject { ["name"] = new JsonObject { ["full"] = "After the expiry" } });
        Assert.True((await store.RefreshAsync(key, Ct)).IsOk);
        Assert.Contains(store.Snapshot(key)!.Cards, c => (string?)c["name"]!["full"] == "After the expiry");
        Assert.Equal(2, server.Count("ContactCard/query"));
        Assert.True((await store.RefreshAsync("never-loaded", Ct)).Error is not null);
    }

    [Fact]
    public async Task A_contact_push_through_the_relay_refreshes_a_loaded_login_and_other_types_do_not()
    {
        var contacts = FakeSeed.ContactsM5(0);
        var rig = ContactRig.Create();
        var (key, server, client) = await rig.AddAsync("user@example.org", contacts);
        using var store = new ContactStore(rig, rig.Hooks, rig.Time, rig.Push);
        _ = await store.LoadAsync(key, Ct);
        var queries = server.Count("ContactCard/changes");
        var added = contacts.AddCard("ab1", new JsonObject { ["name"] = new JsonObject { ["full"] = "Pushed" } });
        rig.Push.Raise(client, ["Email"]);
        await store.DrainAsync();
        Assert.Equal(queries, server.Count("ContactCard/changes"));

        rig.Push.Raise(client, ["ContactCard"]);
        await store.DrainAsync();
        Assert.Contains(store.Snapshot(key)!.Cards, c => (string?)c["id"] == added);
    }

    [Fact]
    public async Task The_engine_hands_a_contact_state_change_to_the_store_over_the_event_source()
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

        // the fake server does not push contact changes by itself (PushHub follows mail and calendars): a StateChange as Stalwart sends it
        var id = contacts.AddCard("ab1", new JsonObject { ["name"] = new JsonObject { ["full"] = "Live" } });
        server.Push.Send("state", new JsonObject { ["@type"] = "StateChange", ["changed"] = new JsonObject { [server.Options.AccountId] = new JsonObject { ["ContactCard"] = contacts.State("ContactCard") } } }.ToJsonString());
        await Poll.UntilAsync(() => rig.Traced("PUSH state ContactCard"), "the push of the new card");
        clock.Advance(MailSync.Coalesce);
        var e = await raised.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        await store.DrainAsync();
        Assert.Contains(id, e.CardIds);
        Assert.Contains(store.Snapshot(key)!.Cards, c => (string?)c["id"] == id);
        await stop.CancelAsync();
        await run;
    }
}
