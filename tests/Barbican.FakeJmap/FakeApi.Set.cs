// The fake server's Email/set: RFC 8621 §4.6 and RFC 8620 §5.3 (update as a PatchObject: a key is a JSON Pointer such as
// `keywords/$seen` or `mailboxIds/<id>`, a null value removes; destroy; notUpdated / notDestroyed SetErrors). Owner jmap.

using System.Text.Json.Nodes;

namespace Barbican.FakeJmap;

public sealed partial class FakeApi
{
    private (string? Name, JsonObject Args) EmailSet(JsonObject args)
    {
        if (((args["update"] as JsonObject)?.Count ?? 0) + ((args["destroy"] as JsonArray)?.Count ?? 0) > options.MaxObjectsInSet)
        {
            return Error("requestTooLarge");
        }

        var oldState = store.State("Email");
        var updated = new JsonObject();
        var notUpdated = new JsonObject();
        foreach (var (id, patch) in args["update"] as JsonObject ?? [])
        {
            if (patch is JsonObject p && store.Patch(id, e => ApplyPatch(e, p)))
            {
                updated[id] = null;
            }
            else
            {
                notUpdated[id] = new JsonObject { ["type"] = patch is JsonObject ? "notFound" : "invalidPatch" };
            }
        }

        var destroyed = new JsonArray();
        var notDestroyed = new JsonObject();
        foreach (var id in (args["destroy"] as JsonArray ?? []).Select(x => (string?)x).OfType<string>())
        {
            if (store.Email(id) is null)
            {
                notDestroyed[id] = new JsonObject { ["type"] = "notFound" };
                continue;
            }

            store.Destroy(id);
            destroyed.Add(id);
        }

        return (null, new JsonObject
        {
            ["accountId"] = options.AccountId,
            ["oldState"] = oldState,
            ["newState"] = store.State("Email"),
            ["updated"] = updated,
            ["notUpdated"] = notUpdated,
            ["destroyed"] = destroyed,
            ["notDestroyed"] = notDestroyed,
        });
    }

    /// <summary>Applies a PatchObject; false when a path is not one this server accepts.</summary>
    public static bool ApplyPatch(JsonObject email, JsonObject patch)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(patch);
        foreach (var (path, value) in patch)
        {
            var parts = path.Split('/').Select(t => t.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal)).ToArray();
            if (parts.Length == 1 && parts[0] is "keywords" or "mailboxIds" && value is JsonObject whole)
            {
                email[parts[0]] = whole.DeepClone();
            }
            else if (parts.Length == 2 && parts[0] is "keywords" or "mailboxIds" && email[parts[0]] is JsonObject map)
            {
                if (value is null)
                {
                    map.Remove(parts[1]);
                }
                else
                {
                    map[parts[1]] = true;
                }
            }
            else
            {
                return false;
            }
        }

        return true;
    }
}
