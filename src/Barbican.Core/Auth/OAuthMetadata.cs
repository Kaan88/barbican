// Mirrors: Rust oauth.rs:17 Meta; RFC 8414 §2 (authorization server metadata: issuer, authorization_response_iss_parameter_supported from
// RFC 9207 §3); Stalwart crates/http/src/auth/oauth/auth.rs:51 OAuthMetadata.

namespace Barbican.Core.Auth;

/// <summary>Where an account signs in.</summary>
/// <param name="Authorization">The authorization endpoint.</param>
/// <param name="Token">The token endpoint.</param>
/// <param name="Registration">The dynamic registration endpoint (RFC 7591), when offered.</param>
/// <param name="Revocation">The revocation endpoint (RFC 7009), when offered.</param>
/// <param name="Scope">The scope to ask for (space separated).</param>
/// <param name="External">True when the endpoints belong to an external SSO provider the server trusts.</param>
public sealed record OAuthMetadata(Uri Authorization, Uri Token, Uri? Registration, Uri? Revocation, string Scope, bool External)
{
    /// <summary>The <c>issuer</c> of the metadata document (RFC 8414 §2), compared with the callback's <c>iss</c> (RFC 9207 §2.4).</summary>
    public string? Issuer { get; init; }

    /// <summary>The provider always sends <c>iss</c> with the authorization response (RFC 9207 §3): a callback without it is refused.</summary>
    public bool IssParameterSupported { get; init; }
}
