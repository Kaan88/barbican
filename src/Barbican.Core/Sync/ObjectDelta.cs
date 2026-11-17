// Mirrors: Rust jmap.rs:1494 apply_object_delta, :1489 split_known; RFC 8620 §5.2 (/changes), RFC 8621 §2.2 (Mailbox/changes
// updatedProperties: only the counts changed); Stalwart crates/jmap/src/changes/get.rs (counts-only container changes). Owner jmap.

using System.Text.Json.Nodes;
using Barbican.Core.Jmap.Json;

namespace Barbican.Core.Sync;

/// <summary>Applies object-level (Mailbox) changes to the objects already held.</summary>
public static class ObjectDelta
{
    /// <summary>Drops the destroyed objects and merges each fetched (possibly partial) object into the one held, or adds it.</summary>
    public static IReadOnlyList<JsonObject> Apply(IReadOnlyList<JsonObject> have, IReadOnlyList<string> destroyed, IReadOnlyList<JsonObject> fetched)
    {
        ArgumentNullException.ThrowIfNull(have);
        ArgumentNullException.ThrowIfNull(destroyed);
        ArgumentNullException.ThrowIfNull(fetched);
        var gone = destroyed.ToHashSet(StringComparer.Ordinal);
        var output = have.Where(m => m.Str("id") is not { } id || !gone.Contains(id)).Select(m => (JsonObject)m.DeepClone()).ToList();
        foreach (var f in fetched)
        {
            var id = f.Str("id");
            var held = id is null ? null : output.Find(m => string.Equals(m.Str("id"), id, StringComparison.Ordinal));
            if (held is null)
            {
                output.Add((JsonObject)f.DeepClone());
                continue;
            }

            foreach (var (key, value) in f)
            {
                held[key] = value?.DeepClone();
            }
        }

        return output;
    }

    /// <summary>
    /// Updated ids to fetch with the changed properties only (<paramref name="partial"/>) and those to fetch whole: an id not held always
    /// needs every property, or it would join the tree with no name, parent or role.
    /// </summary>
    public static (IReadOnlyList<string> Partial, IReadOnlyList<string> Full) SplitKnown(IReadOnlyList<string> ids, IReadOnlyList<JsonObject> have, bool partial)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(have);
        var known = have.Select(m => m.Str("id")).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var part = new List<string>();
        var full = new List<string>();
        foreach (var id in ids)
        {
            (!partial || known.Contains(id) ? part : full).Add(id);
        }

        return (part, full);
    }
}
