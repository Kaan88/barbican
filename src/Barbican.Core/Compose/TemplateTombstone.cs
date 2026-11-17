// Mirrors: Rust templates.rs:78 tombstone; Bulwark lib/jmap/client.ts template metadata (org.bulwarkmail.templates tombstones: { version: 1, deleted: { id: iso } }); Stalwart crates/jmap/src/mailbox/set.rs (metadata).

using System.Globalization;
using System.Text.Json.Nodes;

namespace Barbican.Core.Compose;

/// <summary>The deletion tombstones other Bulwark devices read.</summary>
public static class TemplateTombstone
{
    /// <summary>The tombstone map with <paramref name="id"/> marked deleted at <paramref name="now"/> (ISO 8601, milliseconds, Z); every other deletion is kept; a missing or non-object <paramref name="old"/> starts <c>{ "version": 1, "deleted": {} }</c>.</summary>
    public static JsonObject Mark(JsonNode? old, string id, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(id);
        var map = old is JsonObject o ? (JsonObject)o.DeepClone() : new JsonObject { ["version"] = 1, ["deleted"] = new JsonObject() };
        if (map["deleted"] is not JsonObject deleted)
        {
            deleted = new JsonObject();
            map["deleted"] = deleted;
        }

        map["version"] ??= 1;
        deleted[id] = now.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        return map;
    }
}
