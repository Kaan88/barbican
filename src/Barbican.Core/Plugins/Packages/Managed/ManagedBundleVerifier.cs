// Mirrors Bulwark lib/plugin-sandbox/bundle-signing.ts:21-27,109-135 and bundle-integrity.ts:75-87.
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Math.EC.Rfc8032;

namespace Barbican.Core.Plugins.Packages.Managed;

/// <summary>Verifies exact executable bytes against a host-pinned key and authoritative code digest; conveys no provenance or grant.</summary>
internal static class ManagedBundleVerifier
{
    private const int MaxCodeBytes = 20 * 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static bool Verify(ReadOnlySpan<byte> code, string pinnedPublicKeyBase64, string expectedSha256Hex, string signatureBase64)
    {
        if (code.Length is 0 or > MaxCodeBytes || code.StartsWith(Encoding.UTF8.Preamble) ||
            !CanonicalBase64(pinnedPublicKeyBase64, 32, out var key) || !CanonicalBase64(signatureBase64, 64, out var signature) ||
            !TryDigest(expectedSha256Hex, out var expected)) return false;
        try
        {
            _ = StrictUtf8.GetCharCount(code);
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(code), expected)) return false;
            var point = Ed25519.ValidatePublicKeyFullExport(key);
            if (point is null) return false;
            var verifier = new Ed25519Signer();
            verifier.Init(false, new Ed25519PublicKeyParameters(point));
            verifier.BlockUpdate(code);
            return verifier.VerifySignature(signature);
        }
        catch (Exception error) when (error is ArgumentException or CryptoException or InvalidOperationException)
        {
            return false;
        }
    }

    internal static bool IsValidKey(string? publicKey) =>
        CanonicalBase64(publicKey, 32, out var key) && Ed25519.ValidatePublicKeyFull(key);

    private static bool CanonicalBase64(string? value, int length, out byte[] bytes)
    {
        bytes = [];
        if (value is null || value.Length != ((length + 2) / 3) * 4) return false;
        var decoded = new byte[length];
        if (!Convert.TryFromBase64String(value, decoded, out var written) || written != length ||
            !string.Equals(Convert.ToBase64String(decoded), value, StringComparison.Ordinal)) return false;
        bytes = decoded;
        return true;
    }

    private static bool TryDigest(string? value, out byte[] bytes)
    {
        bytes = [];
        if (value is null || value.Length != 64 || value.Any(c => !char.IsAsciiHexDigit(c))) return false;
        try { bytes = Convert.FromHexString(value); return true; }
        catch (FormatException) { return false; }
    }
}
