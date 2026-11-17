// Mirrors: RFC 8620 §3.3 (the Request object). Contract stub from F2; owner jmap.

using System.Text.Json.Nodes;

namespace Barbican.Core.Jmap;

/// <summary>A JMAP request: capabilities in <c>using</c> and method calls <c>[name, args, callId]</c>.</summary>
public sealed record JmapRequest
{
    /// <summary>The capability URNs.</summary>
    public IReadOnlyList<string> Using { get; init; } = [];

    /// <summary>The method calls, each a JSON array <c>[name, args, callId]</c>.</summary>
    public IReadOnlyList<JsonArray> MethodCalls { get; init; } = [];
}
