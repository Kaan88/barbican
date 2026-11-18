// The fakes of the contacts UI tests (M5 plan section 5: until con-data lands, con-ui works against fakes of the interfaces): an in-memory store, a recording writer, a fixed account, and card builders. Owner con-ui.

#pragma warning disable MA0048, CA1002, CA1819 // the fakes of one harness live in one file
using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Contacts;
using Barbican.Core.Plugins.Hooks;
using Barbican.Presentation.Common;
using Barbican.Presentation.Contacts;
using Barbican.Presentation.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace Barbican.Presentation.Tests.Contacts;

internal sealed class FakeContactStore : IContactStore
{
    public event EventHandler<ContactChangedEventArgs>? Changed;

    public List<JsonNode> Books { get; } = [];

    public List<JsonNode> Cards { get; } = [];

    public int Loads { get; private set; }

    public Task<Result<ContactSnapshot>> LoadAsync(string accountKey, CancellationToken ct)
    {
        Loads++;
        return Task.FromResult(Result<ContactSnapshot>.Ok(Snapshot(accountKey)!));
    }

    public ContactSnapshot? Snapshot(string accountKey) => new(accountKey, [.. Books], [.. Cards], "b1", "c1");

    public Task<Result<Unit>> RefreshAsync(string accountKey, CancellationToken ct) => Task.FromResult(Result<Unit>.Ok(default));

    public JsonNode? Patch(string accountKey, string cardId, JsonObject patch) => null;

    public void Restore(string accountKey, string cardId, JsonNode? previous)
    {
    }

    public void Raise() => Changed?.Invoke(this, new ContactChangedEventArgs("acc", false, []));
}

internal sealed class RecordingContactWriter(FakeContactStore store) : IContactWriter
{
    public List<string> Deleted { get; } = [];

    public List<(string Id, JsonObject Patch)> Updates { get; } = [];

    public List<(string Group, IReadOnlyList<string> Add, IReadOnlyList<string> Remove)> Members { get; } = [];

    public List<JsonObject> Created { get; } = [];

    public Task<Result<string>> CreateAsync(string accountKey, string? bookId, JsonObject card, CancellationToken ct)
    {
        Created.Add(card);
        var id = "new" + Created.Count;
        card["id"] = id;
        store.Cards.Add(card);
        return Task.FromResult(Result<string>.Ok(id));
    }

    public Task<Result<Unit>> UpdateAsync(string accountKey, string cardId, JsonObject patch, CancellationToken ct)
    {
        Updates.Add((cardId, patch));
        return Task.FromResult(Result<Unit>.Ok(default));
    }

    public Task<Result<Unit>> DeleteAsync(string accountKey, IReadOnlyList<string> cardIds, CancellationToken ct)
    {
        Deleted.AddRange(cardIds);
        _ = store.Cards.RemoveAll(c => cardIds.Contains(ContactsJson.Id(c), StringComparer.Ordinal));
        return Task.FromResult(Result<Unit>.Ok(default));
    }

    public Task<Result<Unit>> MoveAsync(string accountKey, IReadOnlyList<string> cardIds, string bookId, CancellationToken ct) => Task.FromResult(Result<Unit>.Ok(default));

    public List<(string Name, IReadOnlyList<string> Uids)> Groups { get; } = [];

    public Task<Result<string>> CreateGroupAsync(string accountKey, string? bookId, string name, IReadOnlyList<string> memberUids, CancellationToken ct)
    {
        Groups.Add((name, memberUids));
        return Task.FromResult(Result<string>.Ok("g-new"));
    }

    public Task<Result<Unit>> SetMembersAsync(string accountKey, string groupId, IReadOnlyList<string> add, IReadOnlyList<string> remove, CancellationToken ct)
    {
        Members.Add((groupId, add, remove));
        return Task.FromResult(Result<Unit>.Ok(default));
    }

    public Task<Result<Unit>> MergeAsync(string accountKey, ContactMergePlan plan, IReadOnlyList<JsonNode> groups, CancellationToken ct) => Task.FromResult(Result<Unit>.Ok(default));

    public Task<Result<int>> ImportAsync(string accountKey, string bookId, IReadOnlyList<JsonNode> cards, CancellationToken ct) => Task.FromResult(Result<int>.Ok(cards.Count));
}

internal sealed class FixedContactsAccount : IContactsAccountSource
{
    public event EventHandler? Changed;

    public string? AccountKey { get; private set; } = "acc";

    public bool HasContacts { get; set; } = true;

    public void Switch(string? key)
    {
        AccountKey = key;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

internal sealed class ContactsHarness : IDisposable
{
    public ContactsHarness(string prefs = "{}")
    {
        Time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero));
        Time.SetLocalTimeZone(TimeZoneInfo.Utc);
        Prefs = new TestPrefs(prefs);
        Bus = new HookBus();
        Store = new FakeContactStore();
        Writer = new RecordingContactWriter(Store);
        Account = new FixedContactsAccount();
        Store.Books.Add(J("""{"id": "b1", "name": "Contacts", "isDefault": true}"""));
        Store.Books.Add(J("""{"id": "b2", "name": "Work"}"""));
        Store.Cards.Add(Card("c1", "Ada Lovelace", "ada@example.org", "b1", "Friends"));
        Store.Cards.Add(Card("c2", "Émile Zola", "emile@example.org", "b2"));
        Store.Cards.Add(Card("c3", "Charles Babbage", "charles@example.org", "b1", "Friends"));
        Store.Cards.Add(J("""{"id": "g1", "uid": "urn:uuid:g1", "kind": "group", "name": {"full": "Engines"}, "members": {"urn:uuid:u-c1": true, "urn:uuid:u-c3": true}, "addressBookIds": {"b1": true}}"""));
        View = new ContactsViewModel(Store, Writer, Prefs, Bus, Time, Account, new InlineDispatcher());
    }

    public FakeTimeProvider Time { get; }

    public TestPrefs Prefs { get; }

    public HookBus Bus { get; }

    public FakeContactStore Store { get; }

    public RecordingContactWriter Writer { get; }

    public FixedContactsAccount Account { get; }

    public ContactsViewModel View { get; }

    public static JsonNode J(string json) => JsonNode.Parse(json)!;

    public static JsonNode Card(string id, string name, string email, string book, params string[] keywords)
    {
        var card = new JsonObject
        {
            ["id"] = id,
            ["uid"] = "urn:uuid:u-" + id,
            ["name"] = new JsonObject { ["full"] = name },
            ["emails"] = new JsonObject { ["e"] = new JsonObject { ["address"] = email } },
            ["addressBookIds"] = new JsonObject { [book] = true },
        };
        if (keywords.Length > 0)
        {
            var map = new JsonObject();
            foreach (var k in keywords)
            {
                map[k] = true;
            }

            card["keywords"] = map;
        }

        return card;
    }

    public static async Task EventuallyAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }
    }

    public void Dispose() => View.Dispose();
}
