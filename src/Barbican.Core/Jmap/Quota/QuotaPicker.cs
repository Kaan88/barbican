// Mirrors: Rust jmap.rs:1157 pick_quota; Bulwark lib/quota.ts; RFC 9425; Stalwart crates/jmap/src/quota/get.rs. Stub from F2; owner jmap.

using Barbican.Core.Common;
using System.Text.Json.Nodes;

namespace Barbican.Core.Jmap.Quota;

/// <summary>Chooses the quota object the storage meter shows.</summary>
public static class QuotaPicker
{
    /// <summary>Prefers an account-scope octets quota covering Mail, then any octets one; null when none.</summary>
    public static JsonObject? Pick(IReadOnlyList<JsonObject> quotas) => throw new PortPendingException("QuotaPicker.Pick: jmap stream");
}
