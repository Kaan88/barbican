// Mirrors: Rust jmap.rs:1494 apply_object_delta, :1489 split_known; RFC 8621 §2.2 (Mailbox/changes updatedProperties). Stub from F2; owner jmap.

using Barbican.Core.Common;
using System.Text.Json.Nodes;

namespace Barbican.Core.Sync;

/// <summary>Applies object-level (Mailbox) changes to the objects already held.</summary>
public static class ObjectDelta
{
    /// <summary>Drops the destroyed objects and merges each fetched (possibly partial) object into the one held, or adds it.</summary>
    public static IReadOnlyList<JsonObject> Apply(IReadOnlyList<JsonObject> have, IReadOnlyList<string> destroyed, IReadOnlyList<JsonObject> fetched) =>
        throw new PortPendingException("ObjectDelta.Apply: jmap stream");

    /// <summary>Updated ids to fetch with the changed properties only (<paramref name="partial"/>) and those to fetch whole: an id not held always needs every property.</summary>
    public static (IReadOnlyList<string> Partial, IReadOnlyList<string> Full) SplitKnown(IReadOnlyList<string> ids, IReadOnlyList<JsonObject> have, bool partial) =>
        throw new PortPendingException("ObjectDelta.SplitKnown: jmap stream");
}
