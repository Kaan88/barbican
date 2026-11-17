// Mirrors: Rust jmap.rs:1374 parse_changes; RFC 8620 §5.2; Stalwart crates/jmap/src/changes/get.rs. Stub from F2; owner jmap.

using Barbican.Core.Common;
using System.Text.Json.Nodes;

namespace Barbican.Core.Sync;

/// <summary>Reads a <c>/changes</c> method response.</summary>
public static class ChangesParser
{
    /// <summary>The changes, or null for a method error (cannotCalculateChanges...) or a Stalwart state that went backwards.</summary>
    public static Changes? Parse(JsonArray response, string since, bool stalwart) => throw new PortPendingException("ChangesParser.Parse: jmap stream");
}
