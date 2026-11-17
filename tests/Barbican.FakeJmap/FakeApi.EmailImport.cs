// Mirrors: RFC 8621 section 4.8 (Email/import: blobId, mailboxIds, keywords, receivedAt; notCreated blobNotFound, invalidProperties);
// Stalwart crates/jmap/src/email/import.rs. The fake reads the blob as the raw message text. Owner actions.

using System.Text.Json.Nodes;

namespace Barbican.FakeJmap;

public sealed partial class FakeApi
{
    private (string? Name, JsonObject Args) EmailImport(JsonObject args)
    {
        var emails = args["emails"] as JsonObject ?? [];
        if (emails.Count > options.MaxObjectsInSet)
        {
            return Error("requestTooLarge");
        }

        var oldState = store.State("Email");
        var created = new JsonObject();
        var notCreated = new JsonObject();
        foreach (var (cid, spec) in emails)
        {
            var o = spec as JsonObject;
            var boxes = (o?["mailboxIds"] as JsonObject)?.Select(kv => kv.Key).ToList() ?? [];
            var keywords = (o?["keywords"] as JsonObject)?.Select(kv => kv.Key).ToList() ?? [];
            if (boxes.Count == 0 || boxes.Exists(b => !store.HasMailbox(b)))
            {
                notCreated[cid] = new JsonObject { ["type"] = "invalidProperties", ["properties"] = new JsonArray("mailboxIds") };
            }
            else if (store.ImportBlob((string?)o?["blobId"] ?? "", boxes, keywords) is { } id)
            {
                created[cid] = new JsonObject { ["id"] = id };
            }
            else
            {
                notCreated[cid] = new JsonObject { ["type"] = "blobNotFound" };
            }
        }

        return (null, new JsonObject
        {
            ["accountId"] = options.AccountId,
            ["oldState"] = oldState,
            ["newState"] = store.State("Email"),
            ["created"] = created,
            ["notCreated"] = notCreated,
        });
    }
}
