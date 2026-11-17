// Mirrors: RFC 8620 §3.4 (the Response object). Contract stub from F2; owner jmap.

using System.Text.Json.Nodes;

namespace Barbican.Core.Jmap;

/// <summary>A JMAP response: <c>methodResponses</c> and the session state.</summary>
public sealed record JmapResponse
{
    /// <summary>The method responses, each <c>[name, args, callId]</c>.</summary>
    public IReadOnlyList<JsonArray> MethodResponses { get; init; } = [];

    /// <summary>The session state the server reported.</summary>
    public string SessionState { get; init; } = "";
}
