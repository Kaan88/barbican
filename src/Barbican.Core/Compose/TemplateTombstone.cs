// Mirrors: Rust templates.rs:78 tombstone; Bulwark lib/jmap/client.ts template metadata (org.bulwarkmail.templates tombstones: { version: 1, deleted: { id: iso } }); Stalwart crates/jmap/src/mailbox/set.rs (metadata). Contract stub from the M3 plan section 4.3; owner send-pipeline replaces the body.

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Compose;

/// <summary>The deletion tombstones other Bulwark devices read (stub until stream send-pipeline lands).</summary>
public static class TemplateTombstone
{
    /// <summary>The tombstone map with <paramref name="id"/> marked deleted at <paramref name="now"/> (ISO 8601, milliseconds, Z); every other deletion is kept; a missing or non-object <paramref name="old"/> starts <c>{ "version": 1, "deleted": {} }</c>.</summary>
    public static JsonObject Mark(JsonNode? old, string id, DateTimeOffset now) => throw new PortPendingException("TemplateTombstone.Mark");
}
