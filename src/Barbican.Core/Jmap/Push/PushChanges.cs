// Mirrors: Rust jmap.rs:1614 changed_types; RFC 8620 §7.1 (StateChange.changed: account id to type to state); Bulwark
// lib/push-events.ts:35 changesAnyType. Owner jmap.

using System.Text.Json.Nodes;
using Barbican.Core.Jmap.Json;

namespace Barbican.Core.Jmap.Push;

/// <summary>Reads a StateChange.</summary>
public static class PushChanges
{
    /// <summary>The changed type to state map of one account (empty when the account is absent).</summary>
    public static IReadOnlyDictionary<string, string> ChangedTypes(JsonNode change, string accountId)
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(accountId);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (change.Obj("changed").Obj(accountId) is { } types)
        {
            foreach (var (type, state) in types)
            {
                if (state.AsString() is { } s)
                {
                    result[type] = s;
                }
            }
        }

        return result;
    }
}
