// The fake server's contact writes (RFC 9610 AddressBook/set with onSuccessSetIsDefault and onDestroyRemoveContents, ContactCard/set create, update with JSON-pointer patches through FakeCalendars.ApplyPatch, destroy; Stalwart crates/jmap/src/addressbook/set.rs, contact/set.rs: a pointer into a map the card
// lacks, such as keywords/x on a card without keywords, is an invalidPatch). Create needs a name for a book and addressBookIds for a card (invalidProperties), a card gets a uid and an updated time, notFound for an unknown id, ifInState, a request log (ContactSetRequests) and FailNextContactSet as FakeApi.CalendarSet.cs has.
// M5 plan section 4.13. Owner con-data.

using System.Text.Json.Nodes;

namespace Barbican.FakeJmap;

public sealed partial class FakeApi
{
    private readonly List<(string Type, JsonObject Args)> _contactSetRequests = [];
    private readonly List<(int Skip, string Type)> _contactSetFailures = [];
    private int _contactClock;

    /// <summary>Every AddressBook/set and ContactCard/set that arrived, with its arguments (a copy).</summary>
    public IReadOnlyList<(string Type, JsonObject Args)> ContactSetRequests
    {
        get
        {
            lock (_lock)
            {
                return [.. _contactSetRequests];
            }
        }
    }

    /// <summary>The next contact write (after <paramref name="skip"/> writes that go through) is refused object by object with this SetError type; nothing changes.</summary>
    public void FailNextContactSet(string errorType, int skip = 0)
    {
        lock (_lock)
        {
            _contactSetFailures.Add((skip, errorType));
        }
    }

    private sealed class ContactRun(string type, JsonObject args, FakeContacts contacts, string? failure)
    {
        public string Type { get; } = type;

        public JsonObject Args { get; } = args;

        public FakeContacts Contacts { get; } = contacts;

        public string? Failure { get; } = failure;

        public bool IsCard { get; } = string.Equals(type, "ContactCard", StringComparison.Ordinal);

        public JsonObject Created { get; } = [];

        public JsonObject Updated { get; } = [];

        public JsonArray Destroyed { get; } = [];

        public JsonObject NotCreated { get; } = [];

        public JsonObject NotUpdated { get; } = [];

        public JsonObject NotDestroyed { get; } = [];

        public string? DefaultTo { get; set; }
    }

    private (string? Name, JsonObject Args) ContactSet(string type, JsonObject args)
    {
        if (options.Contacts is not { } contacts || !options.ContactsCapability)
        {
            return Error("unknownMethod");
        }

        if (contacts.Fails)
        {
            return Error("serverFail");
        }

        string? failure = null;
        lock (_lock)
        {
            _contactSetRequests.Add((type, (JsonObject)args.DeepClone()));
            if (_contactSetFailures.Count > 0 && _contactSetFailures[0].Skip <= 0)
            {
                failure = _contactSetFailures[0].Type;
                _contactSetFailures.RemoveAt(0);
            }
            else if (_contactSetFailures.Count > 0)
            {
                _contactSetFailures[0] = (_contactSetFailures[0].Skip - 1, _contactSetFailures[0].Type);
            }
        }

        var oldState = contacts.State(type);
        if ((string?)args["ifInState"] is { } expected && !string.Equals(expected, oldState, StringComparison.Ordinal))
        {
            return Error("stateMismatch");
        }

        var run = new ContactRun(type, args, contacts, failure);
        ContactCreates(run);
        ContactUpdates(run);
        ContactDestroys(run);
        ContactDefault(run);
        return (null, new JsonObject
        {
            ["accountId"] = options.AccountId,
            ["oldState"] = oldState,
            ["newState"] = contacts.State(type),
            ["created"] = run.Created.Count == 0 ? null : run.Created,
            ["updated"] = run.Updated.Count == 0 ? null : run.Updated,
            ["destroyed"] = run.Destroyed.Count == 0 ? null : run.Destroyed,
            ["notCreated"] = run.NotCreated.Count == 0 ? null : run.NotCreated,
            ["notUpdated"] = run.NotUpdated.Count == 0 ? null : run.NotUpdated,
            ["notDestroyed"] = run.NotDestroyed.Count == 0 ? null : run.NotDestroyed,
        });
    }

    private string ContactNow() => "2026-11-18T10:00:" + (_contactClock++ % 60).ToString("00", System.Globalization.CultureInfo.InvariantCulture) + "Z";

    private void ContactCreates(ContactRun run)
    {
        foreach (var (cid, raw) in run.Args["create"] as JsonObject ?? [])
        {
            if (run.Failure is not null)
            {
                run.NotCreated[cid] = SetError(run.Failure);
                continue;
            }

            var obj = raw is JsonObject o ? (JsonObject)o.DeepClone() : [];
            var bad = run.IsCard ? InvalidCard(run.Contacts, obj) : InvalidBook(obj);
            if (bad is not null)
            {
                run.NotCreated[cid] = SetError("invalidProperties", bad);
                continue;
            }

            string id;
            if (run.IsCard)
            {
                obj["@type"] ??= "Card";
                obj["version"] ??= "1.0";
                obj["uid"] ??= "urn:uuid:" + Guid.NewGuid().ToString("D");
                obj["updated"] = ContactNow();
                var book = ((JsonObject)obj["addressBookIds"]!).First().Key;
                id = run.Contacts.AddCard(book, obj);
                run.Created[cid] = new JsonObject { ["id"] = id, ["uid"] = (string?)obj["uid"], ["updated"] = (string?)obj["updated"] };
            }
            else
            {
                id = run.Contacts.AddBook((string)obj["name"]!, false, obj);
                run.Created[cid] = new JsonObject { ["id"] = id };
            }

            if (string.Equals((string?)run.Args["onSuccessSetIsDefault"], "#" + cid, StringComparison.Ordinal))
            {
                run.DefaultTo = id;
            }
        }
    }

    private void ContactUpdates(ContactRun run)
    {
        foreach (var (id, raw) in run.Args["update"] as JsonObject ?? [])
        {
            if (run.Failure is not null)
            {
                run.NotUpdated[id] = SetError(run.Failure);
                continue;
            }

            var patch = raw as JsonObject ?? [];
            if (run.IsCard && run.Contacts.RefusesBlobMedia && patch.ToJsonString().Contains("\"blobId\"", StringComparison.Ordinal))
            {
                run.NotUpdated[id] = SetError("invalidProperties", "media");
                continue;
            }

            string? error = null;
            var found = run.Contacts.Update(run.Type, id, target =>
            {
                error = BadPatch(target, patch);
                if (error is not null)
                {
                    return;
                }

                foreach (var (path, value) in patch)
                {
                    FakeCalendars.ApplyPatch(target, path, value);
                }

                if (run.IsCard)
                {
                    target["updated"] = ContactNow();
                }
            });
            if (!found)
            {
                run.NotUpdated[id] = SetError("notFound");
            }
            else if (error is not null)
            {
                run.NotUpdated[id] = SetError("invalidPatch", error);
            }
            else
            {
                run.Updated[id] = null;
                if (string.Equals((string?)run.Args["onSuccessSetIsDefault"], id, StringComparison.Ordinal))
                {
                    run.DefaultTo = id;
                }
            }
        }
    }

    private static void ContactDestroys(ContactRun run)
    {
        var removeContents = run.Args["onDestroyRemoveContents"] is JsonValue rv && rv.TryGetValue<bool>(out var rb) && rb;
        foreach (var id in (run.Args["destroy"] as JsonArray ?? []).Select(x => (string?)x).OfType<string>())
        {
            if (run.Failure is not null)
            {
                run.NotDestroyed[id] = SetError(run.Failure);
            }
            else if (run.IsCard)
            {
                if (run.Contacts.Destroy(run.Type, id))
                {
                    run.Destroyed.Add(id);
                }
                else
                {
                    run.NotDestroyed[id] = SetError("notFound");
                }
            }
            else
            {
                DestroyBook(run, id, removeContents);
            }
        }
    }

    private static void DestroyBook(ContactRun run, string id, bool removeContents)
    {
        var book = run.Contacts.Books().FirstOrDefault(b => string.Equals((string?)b["id"], id, StringComparison.Ordinal));
        if (book is null)
        {
            run.NotDestroyed[id] = SetError("notFound");
            return;
        }

        var inside = run.Contacts.Cards().Where(c => (c["addressBookIds"] as JsonObject)?.ContainsKey(id) == true).ToList();
        if (inside.Count > 0 && !removeContents)
        {
            run.NotDestroyed[id] = SetError("addressBookHasContents");
            return;
        }

        foreach (var c in inside)
        {
            _ = run.Contacts.Destroy("ContactCard", (string)c["id"]!);
        }

        _ = run.Contacts.Destroy(run.Type, id);
        run.Destroyed.Add(id);
    }

    private static void ContactDefault(ContactRun run)
    {
        if (run.DefaultTo is null)
        {
            return;
        }

        foreach (var b in run.Contacts.Books())
        {
            var bid = (string)b["id"]!;
            var want = string.Equals(bid, run.DefaultTo, StringComparison.Ordinal);
            if ((bool?)b["isDefault"] != want)
            {
                _ = run.Contacts.Update("AddressBook", bid, t => t["isDefault"] = want);
            }
        }
    }

    private static string? InvalidBook(JsonObject b) => b["name"] is JsonValue v && v.TryGetValue<string>(out var n) && n.Trim().Length > 0 ? null : "name";

    /// <summary>A card needs addressBookIds naming a book that exists; a media entry needs a uri or a blobId.</summary>
    private static string? InvalidCard(FakeContacts contacts, JsonObject c)
    {
        if (c["addressBookIds"] is not JsonObject { Count: > 0 } ids)
        {
            return "addressBookIds";
        }

        var known = contacts.Books().Select(b => (string?)b["id"]).ToList();
        return ids.Any(kv => !known.Contains(kv.Key)) ? "addressBookIds" : null;
    }
}
