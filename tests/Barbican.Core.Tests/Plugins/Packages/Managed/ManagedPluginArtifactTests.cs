using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Packages.Managed;
using Barbican.Core.Plugins.Permissions;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace Barbican.Core.Tests.Plugins.Packages.Managed;

public sealed class ManagedPluginArtifactTests
{
    private const string Id = "managed-probe";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_artifact_binds_exact_code_and_registry_permissions_but_does_not_enable_local_elevation()
    {
        var code = "exports.activate = () => {};"u8.ToArray();
        var signed = Sign(code);
        var registry = Registry(code);
        var built = await ManagedPluginArtifactFactory.CreateAsync(registry, Id, code, signed.Key, signed.Signature, "1.21.2", Ct);
        var repeated = await ManagedPluginArtifactFactory.CreateAsync(registry, Id, code, signed.Key, signed.Signature, "1.21.2", Ct);
        Assert.True(built.IsOk, built.Error?.Message);
        Assert.True(repeated.IsOk, repeated.Error?.Message);
        var artifact = built.Value!;
        Assert.Equal(artifact.Inspection.WholeArchiveSha256, repeated.Value!.Inspection.WholeArchiveSha256, StringComparer.Ordinal);
        Assert.Equal(code, artifact.Inspection.Resources["scripts/plugin.js"].ToArray());
        Assert.Equal(["crypto:full", "email:raw-send"], artifact.Inspection.Permissions.Order(StringComparer.Ordinal), StringComparer.Ordinal);
        Assert.Equal(2, artifact.Inspection.Resources.Count);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Convert.FromBase64String(signed.Key))), artifact.SignerSha256, StringComparer.Ordinal);

        var rig = new PackageTestRig();
        using var manager = rig.Manager();
        using var archive = new MemoryStream(artifact.Archive.ToArray(), writable: false);
        Assert.False((await manager.InspectAsync(archive, PluginPackageSource.Managed, Ct)).IsOk);
        archive.Position = 0;
        var local = await manager.InspectAsync(archive, PluginPackageSource.LocalImport, Ct);
        Assert.True(local.IsOk, local.Error?.Message);
        Assert.Equal(PluginExecutionTier.Privileged, local.Value!.Candidate.RequestedTier);
        var attempted = await manager.InstallAsync(new(local.Value.ReviewId, local.Value.Candidate.Identity,
            local.Value.Candidate.RequestedPermissions, [], approvePrivilegedJavaScript: true), Ct);
        Assert.False(attempted.IsOk);
        Assert.Empty((await manager.ListAsync(Ct)).Value!);
    }

    [Theory]
    [InlineData("untrusted", false, false)]
    [InlineData("privileged", true, false)]
    [InlineData("privileged", false, true)]
    public async Task A_valid_signature_cannot_override_registry_tier_development_or_device_refusal(string tier, bool dev, bool refused)
    {
        var code = "exports.activate = () => {};"u8.ToArray();
        var signed = Sign(code);
        var registry = Registry(code, tier, dev, refused);

        var result = await ManagedPluginArtifactFactory.CreateAsync(registry, Id, code, signed.Key, signed.Signature, "1.21.2", Ct);

        Assert.False(result.IsOk);
    }

    [Fact]
    public async Task A_signed_different_body_and_a_different_key_are_both_rejected()
    {
        var expected = "exports.activate = () => 1;"u8.ToArray();
        var different = "exports.activate = () => 2;"u8.ToArray();
        var signedExpected = Sign(expected);
        var signedDifferent = Sign(different, discriminator: 1);
        var registry = Registry(expected);
        Assert.False((await ManagedPluginArtifactFactory.CreateAsync(registry, Id, different,
            signedDifferent.Key, signedDifferent.Signature, "1.21.2", Ct)).IsOk);
        Assert.False((await ManagedPluginArtifactFactory.CreateAsync(registry, Id, expected,
            signedDifferent.Key, signedExpected.Signature, "1.21.2", Ct)).IsOk);
    }

    private static ManagedPluginRegistry Registry(byte[] code, string tier = "privileged", bool dev = false, bool refused = false)
    {
        var metadata = new JsonObject
        {
            ["plugins"] = new JsonArray(new JsonObject
            {
                ["id"] = Id, ["name"] = "Managed probe", ["author"] = "Example", ["version"] = "1.0.0",
                ["type"] = "hook", ["tier"] = tier, ["dev"] = dev, ["refused"] = refused,
                ["entrypoint"] = "scripts/plugin.js", ["bundleHash"] = Convert.ToHexStringLower(SHA256.HashData(code)),
                ["permissions"] = new JsonArray("crypto:full", "email:raw-send"),
            }),
        };
        using var document = JsonDocument.Parse(metadata.ToJsonString());
        var result = ManagedPluginRegistry.Parse(document.RootElement);
        Assert.True(result.IsOk, result.Error?.Message);
        return result.Value!;
    }

    private static (string Key, string Signature) Sign(byte[] bytes, byte discriminator = 0)
    {
        var seed = new byte[32];
        seed[0] = discriminator;
        var key = new Ed25519PrivateKeyParameters(seed);
        var signer = new Ed25519Signer();
        signer.Init(true, key);
        signer.BlockUpdate(bytes, 0, bytes.Length);
        return (Convert.ToBase64String(key.GeneratePublicKey().GetEncoded()), Convert.ToBase64String(signer.GenerateSignature()));
    }
}
