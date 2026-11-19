// Mirrors: RFC 8621 section 2.5 (Mailbox/set: create, update, destroy, onDestroyRemoveEmails); RFC 8620 §5.3 (maxObjectsInSet -> requestTooLarge);
// Stalwart crates/jmap/src/mailbox/set.rs (mailboxHasEmail, mailboxHasChild, onDestroyRemoveEmails). Owner actions.

using System.Text.Json.Nodes;

namespace Barbican.FakeJmap;

public sealed partial class FakeApi
{
    private (string? Name, JsonObject Args) MailboxSet(JsonObject args)
    {
        var count = ((args["create"] as JsonObject)?.Count ?? 0) + ((args["update"] as JsonObject)?.Count ?? 0) + ((args["destroy"] as JsonArray)?.Count ?? 0);
        if (count > options.MaxObjectsInSet)
        {
            return Error("requestTooLarge");
        }

        var oldState = store.State("Mailbox");
        if ((string?)args["ifInState"] is { } expected && !string.Equals(expected, oldState, StringComparison.Ordinal))
            return Error("stateMismatch");
        var (created, notCreated) = MailboxCreates(args["create"] as JsonObject ?? []);
        var updated = new JsonObject();
        var notUpdated = new JsonObject();
        foreach (var (id, patch) in (args["update"] as JsonObject ?? []).OrderBy(u => u.Value is JsonObject { } q && q.ContainsKey("role") && q["role"] is null ? 0 : 1))
        {
            var err = patch is JsonObject p ? UpdateWithRole(id, p) : "invalidPatch";
            if (err is null)
            {
                updated[id] = null;
            }
            else
            {
                notUpdated[id] = new JsonObject { ["type"] = err };
            }
        }

        var destroyed = new JsonArray();
        var notDestroyed = new JsonObject();
        var removeEmails = args["onDestroyRemoveEmails"] is JsonValue v && v.GetValue<bool>();
        foreach (var id in (args["destroy"] as JsonArray ?? []).Select(x => (string?)x).OfType<string>())
        {
            var err = store.DestroyMailbox(id, removeEmails);
            if (err is null)
            {
                destroyed.Add(id);
            }
            else
            {
                notDestroyed[id] = new JsonObject { ["type"] = err };
            }
        }

        return (null, new JsonObject
        {
            ["accountId"] = options.AccountId,
            ["oldState"] = oldState,
            ["newState"] = store.State("Mailbox"),
            ["created"] = created,
            ["notCreated"] = notCreated,
            ["updated"] = updated,
            ["notUpdated"] = notUpdated,
            ["destroyed"] = destroyed,
            ["notDestroyed"] = notDestroyed,
        });
    }

    // srv-data (M6 folder management): a role is set or cleared through the store's role arm (a role is one folder's: the update that clears the holder is processed first),
    // the rest of the patch goes to the M2 arm; Stalwart crates/jmap/src/mailbox/set.rs
    private string? UpdateWithRole(string id, JsonObject patch)
    {
        if (!patch.ContainsKey("role"))
        {
            return store.UpdateMailbox(id, patch);
        }

        var err = store.SetMailboxRole(id, (string?)patch["role"]);
        var rest = new JsonObject(patch.Where(kv => !string.Equals(kv.Key, "role", StringComparison.Ordinal)).Select(kv => new KeyValuePair<string, JsonNode?>(kv.Key, kv.Value?.DeepClone())));
        return err ?? (rest.Count > 0 ? store.UpdateMailbox(id, rest) : null);
    }

    private (JsonObject Created, JsonObject NotCreated) MailboxCreates(JsonObject creates)
    {
        var created = new JsonObject();
        var notCreated = new JsonObject();
        foreach (var (cid, spec) in creates)
        {
            var o = spec as JsonObject;
            var name = (string?)o?["name"];
            var parent = (string?)o?["parentId"];
            if (string.IsNullOrWhiteSpace(name))
            {
                notCreated[cid] = new JsonObject { ["type"] = "invalidProperties", ["properties"] = new JsonArray("name") };
            }
            else if (parent is not null && !store.HasMailbox(parent))
            {
                notCreated[cid] = new JsonObject { ["type"] = "invalidProperties", ["properties"] = new JsonArray("parentId") };
            }
            else if (store.Mailboxes().Any(m => string.Equals((string?)m["name"], name, StringComparison.OrdinalIgnoreCase) && string.Equals((string?)m["parentId"], parent, StringComparison.Ordinal)))
            {
                notCreated[cid] = new JsonObject { ["type"] = "alreadyExists" };
            }
            else
            {
                created[cid] = new JsonObject { ["id"] = store.AddMailbox(name, (string?)o?["role"], parent) };
            }
        }
        return (created, notCreated);
    }
}
