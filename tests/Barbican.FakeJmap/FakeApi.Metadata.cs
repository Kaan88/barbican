// The fake server's mailbox metadata (urn:ietf:params:jmap:metadata): Mailbox/get serves the `metadata` property, Mailbox/set takes `metadata/<key>` patches (a null value removes an entry) and honours ifInState with the method error
// stateMismatch (RFC 8620 5.3). Stalwart crates/jmap/src/mailbox/set.rs. Anything else in a Mailbox/set goes to the M2 arm. Owner send-pipeline.

using System.Text.Json.Nodes;

namespace Barbican.FakeJmap;

public sealed partial class FakeApi
{
    private (string? Name, JsonObject Args) MailboxSetWithMetadata(JsonObject args)
    {
        BeforeMailboxSet?.Invoke(args);
        var update = args["update"] as JsonObject;
        var metadata = options.MetadataCapability && update is { Count: > 0 } && update.All(u => u.Value is JsonObject p && p.All(k => k.Key.StartsWith("metadata/", StringComparison.Ordinal)));
        if (!metadata)
        {
            return MailboxSet(args);
        }

        var oldState = store.State("Mailbox");
        if ((string?)args["ifInState"] is { } expected && !string.Equals(expected, oldState, StringComparison.Ordinal))
        {
            return Error("stateMismatch");
        }

        var updated = new JsonObject();
        var notUpdated = new JsonObject();
        foreach (var (id, patch) in update!)
        {
            var err = store.UpdateMailboxMetadata(id, (JsonObject)patch!);
            if (err is null)
            {
                updated[id] = null;
            }
            else
            {
                notUpdated[id] = new JsonObject { ["type"] = err };
            }
        }

        return (null, new JsonObject
        {
            ["accountId"] = options.AccountId,
            ["oldState"] = oldState,
            ["newState"] = store.State("Mailbox"),
            ["created"] = new JsonObject(),
            ["notCreated"] = new JsonObject(),
            ["updated"] = updated,
            ["notUpdated"] = notUpdated,
            ["destroyed"] = new JsonArray(),
            ["notDestroyed"] = new JsonObject(),
        });
    }
}
