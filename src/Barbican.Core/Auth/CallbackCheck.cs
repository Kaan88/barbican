// Mirrors: RFC 6749 §10.12 (state binds the callback to this request), RFC 9207 §2.4 (the iss response parameter: when the client knows the
// issuer it compares iss with it exactly; a provider that announces authorization_response_iss_parameter_supported must send it). Fable
// review 1 F-04. Owner accounts (fix-security stream).

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
            // an iss the client cannot compare proves nothing either way (RFC 9207 §2.4 leaves it to the client)
            return IssRequired ? "The sign-in server's metadata names no issuer to compare its RFC 9207 iss with; the sign-in was stopped." : null;
        }

        return string.Equals(iss, Issuer, StringComparison.Ordinal)
            ? null
            : "The answer came from another sign-in server than the one Barbican asked (RFC 9207 iss mismatch); the sign-in was stopped.";
    }
}
