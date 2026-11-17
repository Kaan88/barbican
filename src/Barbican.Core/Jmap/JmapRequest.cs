// Mirrors: RFC 8620 §3.3 (the Request object: using, methodCalls); Rust jmap.rs:272 request (every advertised capability in
// `using`), :283 request_using (an explicit list). Owner jmap.

using System.Text.Json.Nodes;

namespace Barbican.Core.Jmap;

/// <summary>A JMAP request: capabilities in <c>using</c> and method calls <c>[name, args, callId]</c>.</summary>
public sealed record JmapRequest
{
    /// <summary>The capability URNs; empty sends every capability the server advertised (Rust request).</summary>
    public IReadOnlyList<string> Using { get; init; } = [];

    /// <summary>The method calls, each a JSON array <c>[name, args, callId]</c>.</summary>
    public IReadOnlyList<JsonArray> MethodCalls { get; init; } = [];

    /// <summary>A request of the given calls.</summary>
    public static JmapRequest Of(params JsonArray[] calls) => new() { MethodCalls = calls };

    /// <summary>One method call.</summary>
    public static JsonArray Call(string method, JsonObject args, string callId) => [method, args, callId];
}
