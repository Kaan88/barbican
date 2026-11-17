// Mirrors: RFC 8621 section 4.7 (Email/copy: fromAccountId, create by id; notCreated notFound); Stalwart crates/jmap/src/email/copy.rs.
// The fake has one account, so the copy is made inside it; the response shape is what counts. Owner actions.

using System.Text.Json.Nodes;

namespace Barbican.FakeJmap;

public sealed partial class FakeApi
{
    private (string? Name, JsonObject Args) EmailCopy(JsonObject args)
    {
        var create = args["create"] as JsonObject ?? [];
        if (create.Count > options.MaxObjectsInSet)
        {
            return Error("requestTooLarge");
        }

        var oldState = store.State("Email");
        var created = new JsonObject();
        var notCreated = new JsonObject();
        foreach (var (cid, spec) in create)
        {
            var o = spec as JsonObject;
            var boxes = (o?["mailboxIds"] as JsonObject)?.Select(kv => kv.Key).ToList() ?? [];
            var keywords = (o?["keywords"] as JsonObject)?.Select(kv => kv.Key).ToList() ?? [];
            if (boxes.Count == 0 || boxes.Exists(b => !store.HasMailbox(b)))
            {
                notCreated[cid] = new JsonObject { ["type"] = "invalidProperties", ["properties"] = new JsonArray("mailboxIds") };
            }
            else if (store.CopyEmail((string?)o?["id"] ?? "", boxes, keywords) is { } id)
            {
                created[cid] = new JsonObject { ["id"] = id };
            }
            else
            {
                notCreated[cid] = new JsonObject { ["type"] = "notFound" };
            }
        }

        return (null, new JsonObject
        {
            ["fromAccountId"] = args["fromAccountId"]?.DeepClone(),
            ["accountId"] = options.AccountId,
            ["oldState"] = oldState,
            ["newState"] = store.State("Email"),
            ["created"] = created,
            ["notCreated"] = notCreated,
        });
    }
}
