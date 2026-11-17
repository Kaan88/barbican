// Mirrors: Rust jmap.rs:96 read_only; PORT-PLAN.md §4.6 (the ReadOnly allow-list). Stub from F2; owner jmap.

using Barbican.Core.Common;
using System.Text.Json.Nodes;

namespace Barbican.Core.Jmap.Transport;

/// <summary>Decides whether a request may run on a read-only client.</summary>
public static class ReadOnlyGuard
{
    /// <summary>True when every call is a read (get, query, changes, queryChanges, parse...).</summary>
    public static bool IsReadOnly(JsonArray calls) => throw new PortPendingException("ReadOnlyGuard.IsReadOnly: jmap stream");
}
