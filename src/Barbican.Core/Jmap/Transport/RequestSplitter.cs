// Mirrors: Rust jmap.rs:104 split_calls; Bulwark lib/jmap/request-limits.ts; RFC 8620 §2 (maxCallsInRequest). Stub from F2; owner jmap.

using Barbican.Core.Common;
using System.Text.Json.Nodes;

namespace Barbican.Core.Jmap.Transport;

/// <summary>Splits method calls by <c>maxCallsInRequest</c>, unless they are linked by back-references or created ids.</summary>
public static class RequestSplitter
{
    /// <summary>The batches to send; one batch when the calls reference each other.</summary>
    public static IReadOnlyList<IReadOnlyList<JsonNode>> SplitCalls(IReadOnlyList<JsonNode> calls, int max) =>
        throw new PortPendingException("RequestSplitter.SplitCalls: jmap stream");
}
