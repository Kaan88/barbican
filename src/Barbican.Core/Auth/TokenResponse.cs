// Mirrors: Rust oauth.rs:235 parse_tokens: a non-2xx status or a body without access_token is "Token request failed (<status>): <error>
// <error_description>"; a refresh answer without a new refresh token keeps the old one; the expiry is expires_in minus 30 s. RFC 6749 §5.1
// (successful response), §5.2 (error response), §6 (refresh: the server MAY issue a new refresh token). spec B-OAUTH-012.

using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;

namespace Barbican.Core.Auth;

/// <summary>Reads a token endpoint answer.</summary>
public static class TokenResponse
{
    /// <summary>The tokens in <paramref name="response"/>, or an <see cref="AuthError"/> whose message starts "Token request failed (".</summary>
    public static Result<OAuthTokens> Parse(OAuthResponse response, Uri endpoint, string clientId, string? oldRefresh, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(clientId);
        if (response.NetworkError is { } network)
        {
            return Result<OAuthTokens>.Fail(new TransportError(network, Retryable: true));
        }

        var access = response.Str("access_token");
        if (!response.IsSuccess || access is null)
        {
            var message = $"{OAuthRefresh.TokenRequestFailed}{response.StatusText}): {response.Str("error")} {response.Str("error_description")}";
            return Result<OAuthTokens>.Fail(new AuthError(message, NeedsSignIn: false));
        }

        DateTimeOffset? expires = null;
        if (response.Json?["expires_in"] is System.Text.Json.Nodes.JsonValue v && v.TryGetValue<long>(out var seconds) && seconds >= 0)
        {
            expires = now + TimeSpan.FromSeconds(Math.Max(0, seconds - 30));
        }

        return Result<OAuthTokens>.Ok(new OAuthTokens(access, response.Str("refresh_token") ?? oldRefresh, expires, endpoint, clientId));
    }
}
