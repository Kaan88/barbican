// The fake server's contact data (JMAP Contacts RFC 9610: AddressBook, ContactCard as JSContact RFC 9553; Stalwart crates/jmap/src/addressbook/, contact/, crates/jmap-proto/src/object/{addressbook,contact}.rs): address books and cards as JSON objects, a change log per type with state strings for
// /changes. A test hands one to FakeJmapOptions.Contacts; the arms are in FakeApi.Contacts.cs. M5 plan section 4.13. Owner con-data (the reads); con-data also fills FakeApi.ContactsSet.cs.

using System.Globalization;
using System.Text.Json.Nodes;

namespace Barbican.FakeJmap;

/// <summary>An in-memory contacts account.</summary>
public sealed class FakeContacts
{
    private readonly Lock _lock = new();
    private readonly List<JsonObject> _books = [];
    private readonly List<JsonObject> _cards = [];
    private readonly List<(int State, string Type, string Id, char Kind)> _log = [];
    private int _state = 1;
    private int _expiredBefore;
    private int _next;

    /// <summary>True: AddressBook and ContactCard reads of this account answer serverFail.</summary>
    public bool Fails { get; set; }

    /// <summary>True: a ContactCard/set that puts a blobId into media is refused with invalidProperties (media), as Stalwart 0.16 does; a data: URI is taken.</summary>
    public bool RefusesBlobMedia { get; set; }

    /// <summary>Raised after each change with the changed type and its new state (like FakeStore.Changed).</summary>
    public event EventHandler<FakeStateChangedEventArgs>? Changed;

    /// <summary>Adds an address book; returns its id.</summary>
    public string AddBook(string name, bool isDefault = false, JsonObject? extra = null)
    {
        lock (_lock)
        {
            var id = "ab" + (++_next).ToString(CultureInfo.InvariantCulture);
            var b = new JsonObject { ["id"] = id, ["name"] = name, ["isDefault"] = isDefault, ["isSubscribed"] = true, ["sortOrder"] = _books.Count, ["myRights"] = new JsonObject { ["mayRead"] = true, ["mayWrite"] = true, ["mayDelete"] = true } };
            foreach (var (k, v) in extra ?? [])
            {
                b[k] = v?.DeepClone();
            }

            _books.Add(b);
            Log("AddressBook", id, 'c');
            return id;
        }
    }

    /// <summary>Adds a card (a JSContact object; id, uid and addressBookIds are completed); returns its id.</summary>
    public string AddCard(string bookId, JsonObject card)
    {
        ArgumentNullException.ThrowIfNull(card);
        lock (_lock)
        {
            var id = card["id"] is JsonValue v ? (string)v! : "cc" + (++_next).ToString(CultureInfo.InvariantCulture);
            card["id"] = id;
            card["@type"] ??= "Card";
            card["version"] ??= "1.0";
            card["uid"] ??= "urn:uuid:" + Guid.NewGuid().ToString("D");
            card["addressBookIds"] ??= new JsonObject { [bookId] = true };
            _cards.Add(card);
            Log("ContactCard", id, 'c');
            return id;
        }
    }

    public IReadOnlyList<JsonObject> Books()
    {
        lock (_lock)
        {
            return [.. _books.Select(b => (JsonObject)b.DeepClone())];
        }
    }

    public IReadOnlyList<JsonObject> Cards()
    {
        lock (_lock)
        {
            return [.. _cards.Select(c => (JsonObject)c.DeepClone())];
        }
    }

    public string State(string type)
    {
        lock (_lock)
        {
            return type + "-" + _log.Where(l => string.Equals(l.Type, type, StringComparison.Ordinal)).Select(l => l.State).DefaultIfEmpty(1).Max().ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>The server forgets every state issued so far: /changes from one answers cannotCalculateChanges (RFC 8620 5.2).</summary>
    public void ExpireStates()
    {
        lock (_lock)
        {
            _expiredBefore = _state;
        }
    }

    /// <summary>The created, updated and destroyed ids since a state string of <see cref="State"/>; null when the state is unknown (cannotCalculateChanges).</summary>
    public (List<string> Created, List<string> Updated, List<string> Destroyed)? ChangesSince(string type, string since)
    {
        lock (_lock)
        {
            var prefix = type + "-";
            if (!since.StartsWith(prefix, StringComparison.Ordinal) || !int.TryParse(since.AsSpan(prefix.Length), CultureInfo.InvariantCulture, out var s) || s < _expiredBefore)
            {
                return null;
            }

            List<string> created = [];
            List<string> updated = [];
            List<string> destroyed = [];
            foreach (var l in _log.Where(l => l.State > s && string.Equals(l.Type, type, StringComparison.Ordinal)))
            {
                (l.Kind == 'c' ? created : l.Kind == 'u' ? updated : destroyed).Add(l.Id);
            }

            return (created, updated, destroyed);
        }
    }

    /// <summary>Runs <paramref name="change"/> on the book or card with this id under the lock and logs an update; false when there is none.</summary>
    public bool Update(string type, string id, Action<JsonObject> change)
    {
        lock (_lock)
        {
            var target = (string.Equals(type, "AddressBook", StringComparison.Ordinal) ? _books : _cards).Find(o => string.Equals((string?)o["id"], id, StringComparison.Ordinal));
            if (target is null)
            {
                return false;
            }

            change(target);
            Log(type, id, 'u');
            return true;
        }
    }

    public bool Destroy(string type, string id)
    {
        lock (_lock)
        {
            var list = string.Equals(type, "AddressBook", StringComparison.Ordinal) ? _books : _cards;
            var removed = list.RemoveAll(o => string.Equals((string?)o["id"], id, StringComparison.Ordinal)) > 0;
            if (removed)
            {
                Log(type, id, 'd');
            }

            return removed;
        }
    }

    private void Log(string type, string id, char kind)
    {
        _log.Add((++_state, type, id, kind));
        Changed?.Invoke(this, new FakeStateChangedEventArgs(new Dictionary<string, string>(StringComparer.Ordinal) { [type] = State(type) }));
    }
}
