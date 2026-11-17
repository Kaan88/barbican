// Mirrors: RFC 8620 section 6.3 (Blob/copy: fromAccountId, accountId, blobIds; copied, notCopied); Stalwart crates/jmap/src/blob/copy.rs.
// The fake has one account: any fromAccountId is accepted and a known blob is copied to a new id (Save to Files, T-214, J-036). Owner reader.

using System.Text.Json.Nodes;

namespace Barbican.FakeJmap;

public sealed partial class FakeApi
{
    private (string? Name, JsonObject Args) BlobCopy(JsonObject args)
    {
        var copied = new JsonObject();
        var notCopied = new JsonObject();
        foreach (var id in (args["blobIds"] as JsonArray ?? []).Select(x => (string?)x).OfType<string>())
        {
            if (store.Blob(id) is { } bytes)
            {
                copied[id] = store.PutBlob((byte[])bytes.Clone());
            }
            else
            {
                notCopied[id] = new JsonObject { ["type"] = "blobNotFound" };
            }
        }

        return (null, new JsonObject
        {
            ["fromAccountId"] = (string?)args["fromAccountId"],
            ["accountId"] = options.AccountId,
            ["copied"] = copied.Count == 0 ? null : copied,
            ["notCopied"] = notCopied.Count == 0 ? null : notCopied,
        });
    }
}
