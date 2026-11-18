// M5 con-data: the lookup the mail app reads (suggestions ranked, accents ignored, groups left out, one per address, the onProvideRecipientSuggestions transform; View contact by address across logins), the recipient source that adds contacts and groups to the composer's autocomplete, and the store-backed card reader (D-M5-06; B-CON-002).

#pragma warning disable MA0006, MA0002 // assertions compare string literals

using System.Text.Json.Nodes;
using Barbican.Core.Calendar.Editing;
using Barbican.Core.Compose.Recipients;
using Barbican.Core.Contacts;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;
using Barbican.FakeJmap;
using Microsoft.Extensions.DependencyInjection;

namespace Barbican.Core.Tests.Contacts;

public sealed class ContactLookupTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static FakeContacts People()
    {
        var c = new FakeContacts();
        var book = c.AddBook("Contacts", isDefault: true);
        _ = c.AddCard(book, new JsonObject { ["name"] = new JsonObject { ["full"] = "Anna Müller" }, ["emails"] = new JsonObject { ["e0"] = new JsonObject { ["address"] = "anna@x.example" } } });
        _ = c.AddCard(book, new JsonObject { ["name"] = new JsonObject { ["full"] = "Bob" }, ["emails"] = new JsonObject { ["e0"] = new JsonObject { ["address"] = "muller.bob@x.example" }, ["e1"] = new JsonObject { ["address"] = "ANNA@x.example" } } });
        _ = c.AddCard(book, new JsonObject { ["name"] = new JsonObject { ["full"] = "Carl Muller" }, ["emails"] = new JsonObject { ["e0"] = new JsonObject { ["address"] = "carl@x.example" } } });
        _ = c.AddCard(book, new JsonObject { ["kind"] = "group", ["name"] = new JsonObject { ["full"] = "Müller family" }, ["emails"] = new JsonObject { ["e0"] = new JsonObject { ["address"] = "family@x.example" } } });
        return c;
    }

    private static async Task<(ContactRig Rig, string Key, ContactLookup Lookup, ContactStore Store)> StartAsync(FakeContacts contacts)
    {
        var rig = ContactRig.Create();
        var (key, _, _) = await rig.AddAsync("user@example.org", contacts);
        var store = new ContactStore(rig, rig.Hooks, rig.Time);
        return (rig, key, new ContactLookup(store, rig, rig.Hooks), store);
    }

    [Fact]
    public async Task Suggestions_are_ranked_name_first_then_address_then_contains_and_ignore_accents_and_groups()
    {
        var (_, key, lookup, store) = await StartAsync(People());
        Assert.Null(store.Snapshot(key));
        var hits = await lookup.SuggestAsync("muller", 10, Ct);
        Assert.NotNull(store.Snapshot(key));

        // Carl Muller: the name starts with it is not (Carl...): contains; Bob's address starts with it; Anna Müller: contains (surname)
        Assert.Equal(["muller.bob@x.example", "anna@x.example", "carl@x.example"], hits.Select(h => h.Email).ToArray());
        Assert.DoesNotContain(hits, h => h.Email == "family@x.example");
        var first = await lookup.SuggestAsync("ANNA", 10, Ct);
        Assert.Equal(["anna@x.example"], first.Select(h => h.Email).ToArray());
        Assert.Single(await lookup.SuggestAsync("x.example", 1, Ct));
        Assert.Empty(await lookup.SuggestAsync("   ", 10, Ct));
        Assert.Empty(await lookup.SuggestAsync("zzz", 10, Ct));
    }

    [Fact]
    public async Task A_plugin_can_add_and_change_suggestions()
    {
        var (rig, _, lookup, _) = await StartAsync(People());
        rig.Hooks.Transform<ContactSuggestionsPayload>(HookNames.OnProvideRecipientSuggestions, p => p with { Suggestions = [.. p.Suggestions, new ContactSuggestion("From plugin", "plug@x.example")] });
        var hits = await lookup.SuggestAsync("carl", 5, Ct);
        Assert.Equal(["carl@x.example", "plug@x.example"], hits.Select(h => h.Email).ToArray());
        Assert.Equal("From plugin", (string?)hits[1].Card["name"]!["full"]);
        Assert.Equal(string.Empty, hits[1].AccountKey);
    }

    [Fact]
    public async Task A_card_is_found_by_its_address_in_any_login_without_regard_to_case()
    {
        var rig = ContactRig.Create();
        var (a, _, _) = await rig.AddAsync("one@example.org", People());
        var other = new FakeContacts();
        _ = other.AddCard(other.AddBook("Contacts", isDefault: true), new JsonObject { ["name"] = new JsonObject { ["full"] = "Dora" }, ["emails"] = new JsonObject { ["e0"] = new JsonObject { ["address"] = "dora@y.example" } } });
        var (b, _, _) = await rig.AddAsync("two@example.org", other);
        var lookup = new ContactLookup(new ContactStore(rig, rig.Hooks, rig.Time), rig, rig.Hooks);
        var anna = await lookup.FindByEmailAsync(" Anna@X.example ", Ct);
        Assert.Equal((a, "anna@x.example"), (anna!.AccountKey, anna.Email));
        var dora = await lookup.FindByEmailAsync("DORA@y.example", Ct);
        Assert.Equal(b, dora!.AccountKey);
        Assert.Null(await lookup.FindByEmailAsync("nobody@x.example", Ct));
        Assert.Null(await lookup.FindByEmailAsync("family@x.example", Ct));
    }

    [Fact]
    public async Task The_composer_sources_get_contacts_and_groups_with_their_members_and_keep_the_recent_people()
    {
        var (rig, key, _, store) = await StartAsync(FakeSeed.ContactsM5(0));
        var inner = new FixedSources(new SuggestionSources { Recent = [("Recent Rita", "rita@x.example")] });
        var sources = await new ContactRecipientSources(inner, store, rig).LoadAsync(key, Ct);
        Assert.Equal([("Recent Rita", "rita@x.example")], sources.Recent);
        Assert.Contains(("Ada Lovelace", "ada@analytical.example"), sources.Contacts);
        Assert.DoesNotContain(sources.Contacts, c => c.Name == "Friends");
        var friends = Assert.Single(sources.Groups);
        Assert.Equal((FakeSeed.ContactGroupId, "Friends"), (friends.Id, friends.Name));

        // Ada lists two addresses and the preferred one (pref 1) is the member address; Babbage has one
        Assert.Equal(["ada@analytical.example", "charles@analytical.example"], friends.Members.Select(m => m.Email).ToArray());

        // a login without contacts is left as the inner source gives it
        var rig2 = ContactRig.Create();
        var (plain, _, _) = await rig2.AddAsync("plain@example.org", null);
        var same = await new ContactRecipientSources(inner, new ContactStore(rig2, rig2.Hooks, rig2.Time), rig2).LoadAsync(plain, Ct);
        Assert.Empty(same.Contacts);
    }

    [Fact]
    public async Task The_calendars_card_reader_reads_the_store_when_there_is_one()
    {
        var contacts = FakeSeed.ContactsM5(0);
        var (rig, key, _, store) = await StartAsync(contacts);
        var cards = await new ContactCardReader(rig, store).CardsAsync(key, Ct);
        Assert.True(cards.IsOk);
        Assert.Equal(contacts.Cards().Count, cards.Value!.Count);
        Assert.NotNull(store.Snapshot(key));

        // the same call again reads what the store holds now (a write shows at once)
        _ = await store.RefreshAsync(key, Ct);
        _ = contacts.AddCard("ab1", new JsonObject { ["name"] = new JsonObject { ["full"] = "Later" } });
        _ = await store.RefreshAsync(key, Ct);
        Assert.Equal(contacts.Cards().Count, (await new ContactCardReader(rig, store).CardsAsync(key, Ct)).Value!.Count);
    }

    [Fact]
    public async Task The_registration_puts_the_contacts_over_the_composers_source_once()
    {
        var rig = ContactRig.Create();
        var (key, _, _) = await rig.AddAsync("user@example.org", FakeSeed.ContactsM5(0));
        var store = new ContactStore(rig, rig.Hooks, rig.Time);
        var services = new ServiceCollection();
        services.AddSingleton<IRecipientSources>(new FixedSources(new SuggestionSources { Recent = [("Rita", "rita@x.example")] }));
        services.AddBarbicanContactsData();
        services.AddBarbicanContactsData();
        var descriptor = services.Single(d => d.ServiceType == typeof(IRecipientSources));
        var made = Assert.IsType<ContactRecipientSources>(descriptor.ImplementationFactory!(new TinyProvider(new Dictionary<Type, object> { [typeof(IContactStore)] = store, [typeof(Barbican.Core.Accounts.IAccountManager)] = rig })));
        var sources = await made.LoadAsync(key, Ct);
        Assert.Equal([("Rita", "rita@x.example")], sources.Recent);
        Assert.NotEmpty(sources.Contacts);
        Assert.Single(services, d => d.ServiceType == typeof(IContactStore));
    }

    private sealed class TinyProvider(Dictionary<Type, object> services) : IServiceProvider
    {
        public object? GetService(Type serviceType) => services.GetValueOrDefault(serviceType);
    }

    private sealed class FixedSources(SuggestionSources value) : IRecipientSources
    {
        public Task<SuggestionSources> LoadAsync(string accountId, CancellationToken ct) => Task.FromResult(value);
    }
}
