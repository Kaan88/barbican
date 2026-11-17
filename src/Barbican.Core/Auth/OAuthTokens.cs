// Mirrors: Rust oauth.rs:28 Tokens (access, refresh, expiry, token endpoint, client id, the remembered user).

namespace Barbican.Core.Auth;

/// <summary>The tokens of one OAuth session.</summary>
/// <param name="Access">The access token (never stored).</param>
/// <param name="Refresh">The refresh token, when the server issued one.</param>
/// <param name="ExpiresAt">When the access token should be renewed (<c>expires_in</c> minus 30 s), when known.</param>
/// <param name="TokenEndpoint">Where to refresh.</param>
/// <param name="ClientId">The client the tokens belong to.</param>
public sealed record OAuthTokens(string Access, string? Refresh, DateTimeOffset? ExpiresAt, Uri TokenEndpoint, string ClientId)
{
    /// <summary>Hides the tokens from logs and debugger displays.</summary>
    public override string ToString() => $"OAuthTokens {{ TokenEndpoint = {TokenEndpoint}, ClientId = {ClientId}, Refresh = {(Refresh is null ? "none" : "set")} }}";
}
