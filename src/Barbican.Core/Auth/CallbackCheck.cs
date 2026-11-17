// Mirrors: RFC 6749 §10.12 (state binds the callback to this request), RFC 9207 §2.4 (the iss response parameter: when the client knows the
// issuer it compares iss with it exactly; a provider that announces authorization_response_iss_parameter_supported must send it). Fable
// review 1 F-04. Owner accounts (fix-security stream). M1 hotfix (sign-in): an unknown issuer no longer stops the sign-in (see IssuerMismatch).

namespace Barbican.Core.Auth;

/// <summary>What a loopback callback must carry to be accepted.</summary>
/// <param name="State">The state sent with the authorization request.</param>
/// <param name="Issuer">The provider's issuer from its metadata, or null when it named none.</param>
/// <param name="IssRequired">The provider announced <c>authorization_response_iss_parameter_supported</c>.</param>
public sealed record CallbackCheck(string State, string? Issuer, bool IssRequired)
{
    /// <summary>Why a callback's <paramref name="iss"/> is refused, or null when it passes.</summary>
    public string? IssuerMismatch(string? iss)
    {
        if (iss is null)
        {
            return IssRequired ? "The sign-in server did not say who it is (RFC 9207 iss); the sign-in was stopped." : null;
        }

        if (Issuer is null)
        {
            // an iss the client cannot compare proves nothing either way (RFC 9207 §2.4 leaves it to the client). M1 hotfix: this used to stop
            // the sign-in when the provider announced iss support; Stalwart's per-account discovery answers a plain-HTTP connection (a TLS
            // proxy in front) with issuer "" while announcing support, so every sign-in through it failed after the user had signed in.
            // Bulwark lib/desktop/browser-sign-in.ts:170 compares only when both sides are known; so does this. Discovery fills the issuer from
            // the server's own metadata where it can (OAuthDiscovery).
            return null;
        }

        return string.Equals(iss, Issuer, StringComparison.Ordinal)
            ? null
            : "The answer came from another sign-in server than the one Barbican asked (RFC 9207 iss mismatch); the sign-in was stopped.";
    }
}
