// Mirrors: Rust jmap.rs:1157 pick_quota; Bulwark lib/quota.ts; RFC 9425 §4 (Quota: resourceType, scope, types);
// Stalwart crates/jmap/src/quota/get.rs. Owner jmap.

using System.Text.Json.Nodes;
using Barbican.Core.Jmap.Json;

namespace Barbican.Core.Jmap.Quota;

/// <summary>Chooses the quota object the storage meter shows.</summary>
public static class QuotaPicker
{
    /// <summary>
    /// An <c>octets</c> quota: one covering Mail (no <c>types</c>, an empty list, or one naming Mail), account scope first, then the
    /// least room left; else the octets quota with the least room of any kind; null when there is none.
    /// </summary>
    public static JsonObject? Pick(IReadOnlyList<JsonObject> quotas)
    {
        ArgumentNullException.ThrowIfNull(quotas);
        var octets = quotas.Where(q => string.Equals(q.Str("resourceType"), "octets", StringComparison.Ordinal)).ToList();
        var mail = octets.Where(CoversMail)
            .OrderBy(q => string.Equals(q.Str("scope"), "account", StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(Room)
            .FirstOrDefault();
        return mail ?? octets.OrderBy(Room).FirstOrDefault();
    }

    private static bool CoversMail(JsonObject q) =>
        q["types"] is not JsonArray types || types.Count == 0 || types.Any(t => string.Equals(t.AsString(), "Mail", StringComparison.Ordinal));

    private static ulong Room(JsonObject q)
    {
        var hard = q.Number("hardLimit");
        var used = q.Number("used");
        return hard > used ? hard - used : 0;
    }
}
