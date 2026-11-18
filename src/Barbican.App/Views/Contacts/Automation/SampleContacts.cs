#if AUTOMATION
// Mirrors: M4 plan section 6.3 (sample data for hidden runs: SampleCalendarSource); M5 plan section 6.2 step 3 (a hidden run with BARBICAN_SAMPLE=1 shows the Contacts app on made-up contacts without signing in).
// One in-memory contacts account (store, writer and address books in one object) with fictional people at example.org; nothing here is a real mailbox. Only compiled into the Automation configuration.

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Contacts;
using Barbican.Presentation.Contacts;

#pragma warning disable MA0048, MA0051 // the sample source, its account and the seed are one block of sample data
namespace Barbican.App.Views.Contacts.Automation;

/// <summary>The login the sample Contacts app shows.</summary>
public sealed class SampleContactsAccount : IContactsAccountSource
{
    public const string Key = "sample";

    public event EventHandler? Changed
    {
        add { }
        remove { }
    }

    public string? AccountKey => Key;

    public bool HasContacts => true;
}

/// <summary>A made-up server directory for sample runs (three people, a group and a room, at example.org).</summary>
public sealed class SampleContactDirectory : IContactDirectory
{
    public bool IsAvailable(string accountKey) => true;

    public Task<Result<(IReadOnlyList<JsonNode> Principals, bool Fallback)>> LoadAsync(string accountKey, CancellationToken ct)
    {
        IReadOnlyList<JsonNode> principals =
        [
            new JsonObject { ["id"] = "p1", ["type"] = "individual", ["name"] = "Rosalind Franklin", ["email"] = "rosalind@example.org", ["description"] = "Crystallography" },
            new JsonObject { ["id"] = "p2", ["type"] = "individual", ["name"] = "Dennis Ritchie", ["email"] = "dennis@example.org", ["description"] = "Systems" },
            new JsonObject { ["id"] = "p3", ["type"] = "individual", ["name"] = "Hedy Lamarr", ["email"] = "hedy@example.org" },
            new JsonObject { ["id"] = "p4", ["type"] = "group", ["name"] = "All staff", ["email"] = "staff@example.org" },
            new JsonObject { ["id"] = "p5", ["type"] = "resource", ["name"] = "Meeting room 1", ["email"] = "room1@example.org" },
        ];
        return Task.FromResult(Result<(IReadOnlyList<JsonNode> Principals, bool Fallback)>.Ok((principals, false)));
    }
}

/// <summary>An in-memory contact store, writer and address-book service over made-up contacts.</summary>
public sealed class SampleContactStore : IContactStore, IContactWriter, IAddressBooks
{
    private readonly List<JsonNode> _books = [];
    private readonly List<JsonNode> _cards = [];
    private int _next = 100;

    public SampleContactStore()
    {
        _books.Add(JsonNode.Parse("""{"id": "b1", "name": "Contacts", "isDefault": true}""")!);
        _books.Add(JsonNode.Parse("""{"id": "b2", "name": "Work"}""")!);
        Add("Ada Lovelace", "ada@example.org", "b1", "+44 20 7946 0001", "Analytical Engines", "Friends");
        Add("Charles Babbage", "charles@example.org", "b1", "+44 20 7946 0002", "Analytical Engines", "Friends", "Work");
        Add("Émile Zola", "emile@example.org", "b1", "+33 1 23 45 67 89", string.Empty);
        Add("Grace Hopper", "grace@example.org", "b2", "+1 555 0100", "Compilers Ltd", "Work");
        Add("Alan Turing", "alan@example.org", "b2", "+44 20 7946 0003", "Bletchley Park", "Work");
        Add("Katherine Johnson", "katherine@example.org", "b1", string.Empty, "Orbital Maths");
        Add("Linus Torvalds", "linus@example.org", "b2", string.Empty, "Kernel Co");
        Add("Margaret Hamilton", "margaret@example.org", "b1", "+1 555 0101", "Apollo Software", "Friends");
        Add("Tim Berners-Lee", "tim@example.org", "b2", string.Empty, "Web Foundation");
        Add("Barbara Liskov", "barbara@example.org", "b1", string.Empty, "Type Systems");
        Add("42 Club", "club@example.org", "b1", string.Empty, string.Empty);
        Add("Lovelace, Ada", "ada@example.org", "b2", string.Empty, string.Empty);
        _cards.Add(JsonNode.Parse("""{"id": "g1", "uid": "urn:uuid:g1", "kind": "group", "name": {"full": "Engines Team"}, "members": {"urn:uuid:u-1": true, "urn:uuid:u-2": true}, "addressBookIds": {"b1": true}}""")!);
    }

    public event EventHandler<ContactChangedEventArgs>? Changed;

    /// <summary>How many writes the sample took (a scenario reads it).</summary>
    public int Writes { get; private set; }

    public ContactSnapshot? Snapshot(string accountKey) => new(accountKey, [.. _books], [.. _cards], "b1", "c1");

    public Task<Result<ContactSnapshot>> LoadAsync(string accountKey, CancellationToken ct) => Task.FromResult(Result<ContactSnapshot>.Ok(Snapshot(accountKey)!));

    public Task<Result<Unit>> RefreshAsync(string accountKey, CancellationToken ct) => DoneAsync();

    public JsonNode? Patch(string accountKey, string cardId, JsonObject patch) => null;

    public void Restore(string accountKey, string cardId, JsonNode? previous)
    {
    }

    public Task<Result<string>> CreateAsync(string accountKey, string? bookId, JsonObject card, CancellationToken ct)
    {
        var id = "n" + (++_next).ToString(System.Globalization.CultureInfo.InvariantCulture);
        card["id"] = id;
        card["uid"] ??= "urn:uuid:" + id;
        card["addressBookIds"] = new JsonObject { [bookId ?? "b1"] = true };
        _cards.Add(card);
        return WroteAsync(Result<string>.Ok(id));
    }

    public Task<Result<Unit>> UpdateAsync(string accountKey, string cardId, JsonObject patch, CancellationToken ct)
    {
        if (_cards.Find(c => string.Equals(ContactsJson.Id(c), cardId, StringComparison.Ordinal)) is JsonObject card)
        {
            foreach (var (key, value) in patch)
            {
                if (value is null)
                {
                    _ = card.Remove(key);
                }
                else
                {
                    card[key] = value.DeepClone();
                }
            }
        }

        return WroteAsync(Result<Unit>.Ok(default));
    }

    public Task<Result<Unit>> DeleteAsync(string accountKey, IReadOnlyList<string> cardIds, CancellationToken ct)
    {
        _ = _cards.RemoveAll(c => cardIds.Contains(ContactsJson.Id(c), StringComparer.Ordinal));
        return WroteAsync(Result<Unit>.Ok(default));
    }

    public Task<Result<Unit>> MoveAsync(string accountKey, IReadOnlyList<string> cardIds, string bookId, CancellationToken ct)
    {
        foreach (var card in _cards.Where(c => cardIds.Contains(ContactsJson.Id(c), StringComparer.Ordinal)))
        {
            card["addressBookIds"] = new JsonObject { [bookId] = true };
        }

        return WroteAsync(Result<Unit>.Ok(default));
    }

    public Task<Result<string>> CreateGroupAsync(string accountKey, string? bookId, string name, IReadOnlyList<string> memberUids, CancellationToken ct)
    {
        var members = new JsonObject();
        foreach (var uid in memberUids)
        {
            members[uid] = true;
        }

        var id = "g" + (++_next).ToString(System.Globalization.CultureInfo.InvariantCulture);
        _cards.Add(new JsonObject { ["id"] = id, ["uid"] = "urn:uuid:" + id, ["kind"] = "group", ["name"] = new JsonObject { ["full"] = name }, ["members"] = members, ["addressBookIds"] = new JsonObject { [bookId ?? "b1"] = true } });
        return WroteAsync(Result<string>.Ok(id));
    }

    public Task<Result<Unit>> SetMembersAsync(string accountKey, string groupId, IReadOnlyList<string> add, IReadOnlyList<string> remove, CancellationToken ct)
    {
        if (_cards.Find(c => string.Equals(ContactsJson.Id(c), groupId, StringComparison.Ordinal)) is JsonObject group)
        {
            var members = group["members"] as JsonObject ?? [];
            foreach (var uid in add)
            {
                members[uid] = true;
            }

            foreach (var uid in remove)
            {
                _ = members.Remove(uid);
            }

            group["members"] = members;
        }

        return WroteAsync(Result<Unit>.Ok(default));
    }

    public Task<Result<Unit>> MergeAsync(string accountKey, ContactMergePlan plan, IReadOnlyList<JsonNode> groups, CancellationToken ct)
    {
        _ = UpdateAsync(accountKey, ContactsJson.Id(plan.Keep), plan.Patch, ct);
        return DeleteAsync(accountKey, [.. plan.Others.Select(ContactsJson.Id)], ct);
    }

    public Task<Result<int>> ImportAsync(string accountKey, string bookId, IReadOnlyList<JsonNode> cards, CancellationToken ct)
    {
        foreach (var card in cards)
        {
            _ = CreateAsync(accountKey, bookId, (JsonObject)card, ct);
        }

        return Task.FromResult(Result<int>.Ok(cards.Count));
    }

    public Task<Result<string>> EnsureDefaultAsync(string accountKey, CancellationToken ct) => Task.FromResult(Result<string>.Ok("b1"));

    public Task<Result<string>> CreateAsync(string accountKey, string name, CancellationToken ct)
    {
        var id = "b" + (++_next).ToString(System.Globalization.CultureInfo.InvariantCulture);
        _books.Add(new JsonObject { ["id"] = id, ["name"] = name });
        return WroteAsync(Result<string>.Ok(id));
    }

    public Task<Result<Unit>> RenameAsync(string accountKey, string bookId, string name, CancellationToken ct)
    {
        if (_books.Find(b => string.Equals(ContactsJson.Id(b), bookId, StringComparison.Ordinal)) is JsonObject book)
        {
            book["name"] = name;
        }

        return WroteAsync(Result<Unit>.Ok(default));
    }

    public Task<Result<Unit>> DeleteAsync(string accountKey, string bookId, bool removeContacts, CancellationToken ct)
    {
        _ = _books.RemoveAll(b => string.Equals(ContactsJson.Id(b), bookId, StringComparison.Ordinal));
        if (removeContacts)
        {
            _ = _cards.RemoveAll(c => ContactsJson.Keys(c, "addressBookIds").Contains(bookId, StringComparer.Ordinal));
        }

        return WroteAsync(Result<Unit>.Ok(default));
    }

    private static Task<Result<Unit>> DoneAsync() => Task.FromResult(Result<Unit>.Ok(default));

    private Task<Result<T>> WroteAsync<T>(Result<T> result)
    {
        Writes++;
        Changed?.Invoke(this, new ContactChangedEventArgs(SampleContactsAccount.Key, true, []));
        return Task.FromResult(result);
    }

    private void Add(string name, string email, string book, string phone, string org, params string[] keywords)
    {
        var id = (_cards.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var card = new JsonObject
        {
            ["id"] = id,
            ["uid"] = "urn:uuid:u-" + id,
            ["name"] = new JsonObject { ["full"] = name },
            ["emails"] = new JsonObject { ["e1"] = new JsonObject { ["address"] = email } },
            ["addressBookIds"] = new JsonObject { [book] = true },
        };
        if (phone.Length > 0)
        {
            card["phones"] = new JsonObject { ["p1"] = new JsonObject { ["number"] = phone, ["features"] = new JsonObject { ["mobile"] = true } } };
        }

        if (org.Length > 0)
        {
            card["organizations"] = new JsonObject { ["o1"] = new JsonObject { ["name"] = org } };
        }

        if (keywords.Length > 0)
        {
            var map = new JsonObject();
            foreach (var k in keywords)
            {
                map[k] = true;
            }

            card["keywords"] = map;
        }

        _cards.Add(card);
    }
}
#endif
