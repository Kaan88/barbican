// Mirrors: Rust oauth.rs:121 random (getrandom, base64url without padding), :171-:174 (a 48-byte verifier, its S256 challenge, a 16-byte
// state); RFC 7636 §4.1 (verifier 43-128 characters of the unreserved set), §4.2 (S256 = BASE64URL(SHA256(ASCII(verifier)))); RFC 6749
// §10.12 (state binds the callback to this request). Bulwark lib/oauth/pkce.ts does the same with 32 bytes.

using System.Security.Cryptography;
using System.Text;

namespace Barbican.Core.Auth;

/// <summary>PKCE verifier and challenge, and the random <c>state</c> of an authorization request.</summary>
public static class Pkce
{
    /// <summary>A new verifier: 48 random bytes, base64url (64 characters).</summary>
    public static string NewVerifier() => Random(48);

    /// <summary>A new state: 16 random bytes, base64url.</summary>
    public static string NewState() => Random(16);

    /// <summary>The S256 challenge of <paramref name="verifier"/>.</summary>
    public static string Challenge(string verifier)
    {
        ArgumentNullException.ThrowIfNull(verifier);
        return Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    }

    /// <summary>Base64url without padding (RFC 4648 §5).</summary>
    public static string Base64Url(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Random(int n)
    {
        Span<byte> buf = stackalloc byte[n];
        RandomNumberGenerator.Fill(buf);
        var s = Base64Url(buf);
        CryptographicOperations.ZeroMemory(buf);
        return s;
    }
}
