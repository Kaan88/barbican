// Mirrors: Rust oauth.rs:235 parse_tokens (status plus JSON body; a body that is not JSON reads as {}).

using System.Net;
using System.Text.Json.Nodes;

namespace Barbican.Core.Auth;

/// <summary>An OAuth HTTP answer: status, reason, the JSON object (null when the body was not one), or the network error.</summary>
public sealed record OAuthResponse(HttpStatusCode Status, string Reason, JsonObject? Json, string? NetworkError)
{
    /// <summary>A 2xx answer.</summary>
    public bool IsSuccess => NetworkError is null && (int)Status is >= 200 and < 300;

    /// <summary>A string member of the JSON body, or null.</summary>
    public string? Str(string name) => Json?[name] is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0 ? s : null;

    /// <summary>The status as reqwest prints it: "400 Bad Request".</summary>
    public string StatusText => $"{(int)Status} {Reason}".TrimEnd();
}
