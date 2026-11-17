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
        var (created, notCreated) = MailboxCreates(args["create"] as JsonObject ?? []);
        var updated = new JsonObject();
        var notUpdated = new JsonObject();
        foreach (var (id, patch) in args["update"] as JsonObject ?? [])
        {
            var err = patch is JsonObject p ? store.UpdateMailbox(id, p) : "invalidPatch";
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
