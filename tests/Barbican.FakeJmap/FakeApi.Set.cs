// The fake server's Email/set: RFC 8621 §4.6 and RFC 8620 §5.3 (strict like Stalwart since M2 hotfix 2: no empty mailboxIds, no unknown mailbox) (update as a PatchObject: a key is a JSON Pointer such as
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
        var known = store.Mailboxes().Select(m => (string?)m["id"]).OfType<string>().ToHashSet(StringComparer.Ordinal);
        foreach (var (id, patch) in args["update"] as JsonObject ?? [])
        {
            if (patch is not JsonObject p)
            {
                notUpdated[id] = new JsonObject { ["type"] = "invalidPatch" };
                continue;
            }

            if (store.Email(id) is null)
            {
                notUpdated[id] = new JsonObject { ["type"] = "notFound" };
                continue;
            }

            string? refusal = null;
            if (store.Patch(id, e => (refusal = Check(e, p, known)) is null))
            {
                updated[id] = null;
            }
            else
            {
                notUpdated[id] = new JsonObject { ["type"] = refusal ?? "notFound" };
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

    /// <summary>
    /// Applies a PatchObject to a copy and checks it as Stalwart does (crates/jmap/src/email/set.rs:978-1020): a message must stay in at least one
    /// mailbox (RFC 8621 4.1.1), and every mailbox it is put into must exist. Returns the SetError type, or null when the patch is accepted.
    /// </summary>
    private static string? Check(JsonObject email, JsonObject patch, HashSet<string> mailboxes)
    {
        var before = ((email["mailboxIds"] as JsonObject) ?? []).Select(kv => kv.Key).ToHashSet(StringComparer.Ordinal);
        if (!ApplyPatch(email, patch))
        {
            return "invalidPatch";
        }

        var after = ((email["mailboxIds"] as JsonObject) ?? []).Select(kv => kv.Key).ToList();
        if (after.Count == 0)
        {
            return "invalidProperties";
        }

        return after.Any(m => !before.Contains(m) && !mailboxes.Contains(m)) ? "invalidProperties" : null;
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
                // a whole set: only the true entries count (RFC 8621 4.1.1: every value is true)
                email[parts[0]] = new JsonObject(whole.Where(kv => kv.Value is JsonValue v && v.TryGetValue<bool>(out var on) && on).Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)true)));
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
