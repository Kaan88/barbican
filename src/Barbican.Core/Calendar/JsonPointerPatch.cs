// Mirrors: RFC 8620 5.3 (a patch is a map of JSON pointers to values; null removes), Rust calendar.rs:1308 patch_local (top-level keys only; the pointer form is what Bulwark's local patches use). Owner cal-data.

using System.Text.Json.Nodes;

namespace Barbican.Core.Calendar;

/// <summary>Applies a JMAP patch object to a JSON object in place.</summary>
internal static class JsonPointerPatch
{
    public static void Apply(JsonObject target, JsonObject patch)
    {
        foreach (var (path, value) in patch)
        {
            Set(target, path, value?.DeepClone());
        }
    }

    private static void Set(JsonObject target, string path, JsonNode? value)
    {
        var parts = path.Split('/').Select(p => p.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal)).ToArray();
        var node = target;
        foreach (var p in parts.Take(parts.Length - 1))
        {
            if (node[p] is not JsonObject next)
            {
                if (value is null)
                {
                    return;
                }

                next = [];
                node[p] = next;
            }

            node = next;
        }

        if (value is null)
        {
            _ = node.Remove(parts[^1]);
        }
        else
        {
            node[parts[^1]] = value;
        }
    }
}
