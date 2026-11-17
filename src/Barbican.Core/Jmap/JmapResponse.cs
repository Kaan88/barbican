// Mirrors: RFC 8620 §3.4 (the Response object: methodResponses, sessionState), §3.6.2 (an `error` response stands in for the call);
// Rust jmap.rs:288 call (fails on the first method error), :272 request (returns them). Owner jmap.

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Json;

namespace Barbican.Core.Jmap;

/// <summary>A JMAP response: <c>methodResponses</c> and the session state.</summary>
public sealed record JmapResponse
{
    /// <summary>The method responses, each <c>[name, args, callId]</c>.</summary>
    public IReadOnlyList<JsonArray> MethodResponses { get; init; } = [];

    /// <summary>The session state the server reported.</summary>
    public string SessionState { get; init; } = "";

    /// <summary>The first response for <paramref name="callId"/>, or null.</summary>
    public JsonArray? ByCallId(string callId) =>
        MethodResponses.FirstOrDefault(r => r.Count > 2 && string.Equals(r[2].AsString(), callId, StringComparison.Ordinal));

    /// <summary>
    /// The arguments of the response to <paramref name="callId"/>, or the <see cref="MethodError"/> the server sent instead
    /// (named after <paramref name="method"/>, the call that was made).
    /// </summary>
    public Result<JsonObject> Args(string callId, string method)
    {
        var r = ByCallId(callId);
        if (r is null)
        {
            return Result<JsonObject>.Fail(new MethodError(method, "missingResponse", $"no response for call {callId}")
            {
                Message = "The server did not answer every call.",
            });
        }

        if (string.Equals(r[0].AsString(), "error", StringComparison.Ordinal))
        {
            return Result<JsonObject>.Fail(MethodErrors.From(method, r.Count > 1 ? r[1] : null));
        }

        return r.Count > 1 && r[1] is JsonObject args ? Result<JsonObject>.Ok(args) : Result<JsonObject>.Ok([]);
    }

    /// <summary>The first <c>error</c> response, if any, as a <see cref="MethodError"/>.</summary>
    public MethodError? FirstError(Func<string, string> methodOfCall)
    {
        ArgumentNullException.ThrowIfNull(methodOfCall);
        var e = MethodResponses.FirstOrDefault(r => r.Count > 1 && string.Equals(r[0].AsString(), "error", StringComparison.Ordinal));
        return e is null ? null : MethodErrors.From(methodOfCall(e.Count > 2 ? e[2].AsString() ?? "" : ""), e[1]);
    }
}
