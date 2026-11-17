// Mirrors: Rust jmap.rs:1614 changed_types; RFC 8620 §7.1 (StateChange.changed per account). Stub from F2; owner jmap.

using Barbican.Core.Common;
using System.Text.Json.Nodes;

namespace Barbican.Core.Jmap.Push;

/// <summary>Reads a StateChange.</summary>
public static class PushChanges
{
    /// <summary>The changed type to state map of one account (empty when the account is absent).</summary>
    public static IReadOnlyDictionary<string, string> ChangedTypes(JsonNode change, string accountId) =>
        throw new PortPendingException("PushChanges.ChangedTypes: jmap stream");
}
