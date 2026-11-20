// RFC 8032 section 7.1 test vector 2; all other signing keys below are synthetic fixtures.
using System.Security.Cryptography;
using System.Text;
using Barbican.Core.Plugins.Packages.Managed;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace Barbican.Core.Tests.Plugins.Packages.Managed;

public sealed class ManagedBundleVerifierTests
{
    private static readonly byte[] VectorKey = Convert.FromHexString("3d4017c3e843895a92b70aa74d1b7ebc9c982ccf2ec4968cc0cd55f12af4660c");
    private static readonly byte[] VectorSignature = Convert.FromHexString("92a009a9f0d4cab8720e820b5f642540a2b27b5416503f8fb3762223ebdb69da085ac1e43e15996e458f3613d0f11d8c387b2eaeb4302aeeb00d291612bb0c00");

    [Fact]
    public void PublishedEd25519VectorAndExactHashVerify()
    {
        byte[] code = [0x72];
        Assert.True(ManagedBundleVerifier.Verify(code, Convert.ToBase64String(VectorKey), Hash(code), Convert.ToBase64String(VectorSignature)));
        Assert.False(ManagedBundleVerifier.Verify("s"u8, Convert.ToBase64String(VectorKey), Hash("s"u8), Convert.ToBase64String(VectorSignature)));
        Assert.False(ManagedBundleVerifier.Verify(code, Convert.ToBase64String(VectorKey), Hash("s"u8), Convert.ToBase64String(VectorSignature)));
    }

    [Fact]
    public void SyntheticSignedUtf8RequiresTheExactBytesAndKey()
    {
        var privateKey = new Ed25519PrivateKeyParameters(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());
        var signer = new Ed25519Signer();
        signer.Init(true, privateKey);
        var code = Encoding.UTF8.GetBytes("const greeting = 'Merhaba d\u00FCnya';\n");
        signer.BlockUpdate(code);
        var signature = signer.GenerateSignature();
        var key = privateKey.GeneratePublicKey().GetEncoded();
        Assert.True(ManagedBundleVerifier.Verify(code, Convert.ToBase64String(key), Hash(code), Convert.ToBase64String(signature)));
        code[^1] = (byte)' ';
        Assert.False(ManagedBundleVerifier.Verify(code, Convert.ToBase64String(key), Hash(code), Convert.ToBase64String(signature)));
        code[^1] = 10;
        key[0] ^= 1;
        Assert.False(ManagedBundleVerifier.Verify(code, Convert.ToBase64String(key), Hash(code), Convert.ToBase64String(signature)));
    }

    [Fact]
    public void A_small_order_key_cannot_be_pinned()
    {
        var identity = new byte[32];
        identity[0] = 1;
        Assert.False(ManagedBundleVerifier.IsValidKey(Convert.ToBase64String(identity)));
        Assert.True(ManagedBundleVerifier.IsValidKey(Convert.ToBase64String(VectorKey)));
    }

    [Fact]
    public void RejectsNoncanonicalInputsAndAmbiguousCode()
    {
        byte[] code = [0x72];
        var key = Convert.ToBase64String(VectorKey);
        var signature = Convert.ToBase64String(VectorSignature);
        Assert.False(ManagedBundleVerifier.Verify([], key, Hash(code), signature));
        Assert.False(ManagedBundleVerifier.Verify([0xef, 0xbb, 0xbf, 0x72], key, Hash(code), signature));
        Assert.False(ManagedBundleVerifier.Verify([0xff], key, Hash(code), signature));
        Assert.False(ManagedBundleVerifier.Verify(code, key + " ", Hash(code), signature));
        Assert.False(ManagedBundleVerifier.Verify(code, AlternatePaddingBits(key), Hash(code), signature));
        Assert.False(ManagedBundleVerifier.Verify(code, key, Hash(code), AlternatePaddingBits(signature)));
        Assert.False(ManagedBundleVerifier.Verify(code, Convert.ToBase64String(new byte[31]), Hash(code), signature));
        Assert.False(ManagedBundleVerifier.Verify(code, key, Hash(code), Convert.ToBase64String(new byte[63])));
        Assert.False(ManagedBundleVerifier.Verify(code, key, "not-a-sha256", signature));
        Assert.False(ManagedBundleVerifier.Verify(new byte[(20 * 1024 * 1024) + 1], key, Hash(code), signature));
    }

    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string AlternatePaddingBits(string value)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
        var chars = value.ToCharArray();
        var index = value.IndexOf('=') - 1;
        chars[index] = alphabet[alphabet.IndexOf(chars[index], StringComparison.Ordinal) | 1];
        return new string(chars);
    }
}
