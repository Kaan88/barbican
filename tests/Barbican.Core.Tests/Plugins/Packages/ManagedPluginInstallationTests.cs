// Managed lifecycle: verified synthetic bundle + host provenance + separate privileged consent.
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Packages.Managed;
using Barbican.Core.Plugins.Permissions;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace Barbican.Core.Tests.Plugins.Packages;

public sealed class ManagedPluginInstallationTests
{
    private const string Id = "managed-probe";
    private const string Account = "sample-account";
    private static readonly Uri Origin = new("https://policy.example.org/");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PreviewBindsSignerOriginAndAccountAndConsentMustMatchExactly()
    {
        var rig = new PackageTestRig();
        using var manager = rig.Manager();
        var candidate = await CandidateAsync();
        var preview = Need(await manager.ReviewManagedCandidateAsync(candidate, Ct));
        Assert.Equal(candidate.Artifact.SignerSha256, preview.VerifiedSigner, StringComparer.Ordinal);
        Assert.True(preview.ManagedApprovalVerified);
        Assert.Equal(Origin, preview.ManagedOrigin);
        Assert.Equal(Account, preview.ManagedAccountKey, StringComparer.Ordinal);
        Assert.Equal(PluginPackageSource.Managed, preview.Candidate.Source);
        Assert.Equal(PluginExecutionTier.Privileged, preview.Candidate.RequestedTier);
        Assert.False((await manager.InstallAsync(Decision(preview, [Account], approved: false), Ct)).IsOk);
        Assert.False((await manager.InstallAsync(Decision(preview, [], approved: true), Ct)).IsOk);
        Assert.False((await manager.InstallAsync(Decision(preview, ["different-account"], approved: true), Ct)).IsOk);
        Assert.False((await manager.InstallAsync(Decision(preview, [Account, "different-account"], approved: true), Ct)).IsOk);
        var installed = Need(await manager.InstallAsync(Decision(preview, [Account], approved: true), Ct));
        Assert.False(installed.Enabled);
        Assert.Equal(PluginExecutionTier.Privileged, installed.Grant!.Tier);
        Assert.False(installed.Grant.Enabled);
        Assert.Null(Need(await manager.GetCurrentAsync(Id, Ct)));
    }

    [Fact]
    public async Task RestartPreservesDisabledManagedRecordAndNeverEnablesOrOpensExecutable()
    {
        var rig = new PackageTestRig();
        var candidate = await CandidateAsync();
        using (var manager = rig.Manager())
        {
            var preview = Need(await manager.ReviewManagedCandidateAsync(candidate, Ct));
            _ = Need(await manager.InstallAsync(Decision(preview, [Account], approved: true), Ct));
        }
        using var reopened = rig.Manager();
        var installed = Assert.Single(Need(await reopened.ListAsync(Ct)));
        Assert.False(installed.Enabled);
        Assert.Equal(PluginPackageSource.Managed, installed.Package.Source);
        Assert.Equal(PluginExecutionTier.Privileged, installed.Grant!.Tier);
        Assert.Null(Need(await reopened.GetCurrentAsync(Id, Ct)));
        Assert.False((await reopened.SetEnabledAsync(installed.Package.Identity, true, Ct)).IsOk);
        Assert.False((await reopened.OpenResourceAsync(installed.Package.Identity, installed.Package.Entrypoint, Ct)).IsOk);
        using var local = PackageTestRig.ArchiveForId(Id, "export default 1");
        var localReview = await reopened.InspectAsync(local, PluginPackageSource.LocalImport, Ct);
        if (localReview.IsOk) Assert.False((await reopened.InstallAsync(Decision(localReview.Value!, [], approved: false), Ct)).IsOk);
        Assert.Equal(installed.Package.Identity, Need(await reopened.FindAsync(Id, Ct))!.Package.Identity);
    }

    [Fact]
    public async Task CandidateBecomingStaleBetweenReviewAndInstallDoesNotPersist()
    {
        var rig = new PackageTestRig();
        using var manager = rig.Manager();
        var current = true;
        var candidate = await CandidateAsync(current: issue => current && issue());
        var preview = Need(await manager.ReviewManagedCandidateAsync(candidate, Ct));
        current = false;
        Assert.False((await manager.InstallAsync(Decision(preview, [Account], approved: true), Ct)).IsOk);
        Assert.Empty(Need(await manager.ListAsync(Ct)));
    }

    [Fact]
    public async Task StartupProofMatchesExactArchiveAndOnlyReadsWhileProviderIsCurrent()
    {
        const string entrypoint = "exports.activate = () => 'approved';";
        var rig = new PackageTestRig();
        using var manager = rig.Manager();
        var authorized = true;
        var verified = await CandidateAsync(entrypoint, current: issue => authorized && issue());
        var installed = await InstallManagedAsync(manager, verified);
        var changedRegistry = await CandidateAsync(entrypoint, name: "Changed registry metadata");
        var package = installed.Package.Identity;

        Assert.False((await manager.VerifyManagedStartupAsync(package, (_, _) =>
            Task.FromResult(Result<ManagedPluginCandidate>.Ok(changedRegistry)), Ct)).IsOk);
        var proof = Need(await manager.VerifyManagedStartupAsync(package, (approval, fence) =>
        {
            Assert.Equal(verified.Artifact.SignerSha256, approval.SignerSha256, StringComparer.Ordinal);
            Assert.Equal(Account, approval.AccountKey, StringComparer.Ordinal);
            Assert.True(fence(static () => true));
            return Task.FromResult(Result<ManagedPluginCandidate>.Ok(verified));
        }, Ct));
        Assert.Equal(package, proof.Package);
        Assert.Equal(installed.Grant!.Generation, proof.InstalledGeneration);
        Assert.True(proof.TryReadEntrypoint(code => string.Equals(entrypoint, code, StringComparison.Ordinal)));
        authorized = false;
        Assert.False(proof.TryReadEntrypoint(_ => throw new InvalidOperationException("Revoked proof released code.")));
        Assert.Null(Need(await manager.GetCurrentAsync(Id, Ct)));
    }

    [Fact]
    public async Task RemovingThePackageClosesBothExistingProofAndPendingRequestFence()
    {
        var rig = new PackageTestRig();
        using var manager = rig.Manager();
        var candidate = await CandidateAsync();
        var installed = await InstallManagedAsync(manager, candidate);
        var package = installed.Package.Identity;
        var found = Result<ManagedPluginCandidate>.Ok(candidate);
        var proof = Need(await manager.VerifyManagedStartupAsync(package, (_, _) => Task.FromResult(found), Ct));
        var preparing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var proceed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = manager.VerifyManagedStartupAsync(package, async (_, fence) =>
        {
            preparing.SetResult();
            await proceed.Task.WaitAsync(Ct);
            Assert.False(fence(static () => throw new InvalidOperationException("Removed package could make a request.")));
            return found;
        }, Ct);
        await preparing.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        Assert.True((await manager.UninstallAsync(package, Ct)).IsOk);
        proceed.SetResult();
        Assert.False((await pending).IsOk);
        Assert.False(proof.TryReadEntrypoint(_ => throw new InvalidOperationException("Removed package released code.")));

        var reinstalled = await InstallManagedAsync(manager, candidate);
        Assert.Equal(package, reinstalled.Package.Identity);
        Assert.False(proof.TryReadEntrypoint(_ => throw new InvalidOperationException("A new install restored the old proof.")));
    }

    [Fact]
    public async Task RevocationBeforeGuardedPublicationLeavesNoInstalledRecordOrGrantAfterRestart()
    {
        var rig = new PackageTestRig();
        using var manager = rig.Manager();
        var live = 1;
        var candidate = await CandidateAsync(current: issue => Volatile.Read(ref live) == 1 && issue());
        var preview = Need(await manager.ReviewManagedCandidateAsync(candidate, Ct));
        rig.Persistence.PublishEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Persistence.ReleasePublish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = manager.InstallAsync(Decision(preview, [Account], approved: true), Ct);
        await rig.Persistence.PublishEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        Volatile.Write(ref live, 0);
        rig.Persistence.ReleasePublish.SetResult();
        Assert.False((await pending).IsOk);
        Assert.Empty(Need(await manager.ListAsync(Ct)));
        Assert.Null(Need(await manager.GetCurrentAsync(Id, Ct)));
        using var reopened = rig.Manager();
        Assert.Empty(Need(await reopened.ListAsync(Ct)));
        Assert.Null(Need(await reopened.GetCurrentAsync(Id, Ct)));
    }

    [Theory]
    [InlineData("origin")]
    [InlineData("account")]
    [InlineData("signer")]
    public async Task UpdateFromDifferentManagedSourceCannotReplaceExisting(string difference)
    {
        var rig = new PackageTestRig();
        using var manager = rig.Manager();
        var original = await CandidateAsync();
        var preview = Need(await manager.ReviewManagedCandidateAsync(original, Ct));
        var installed = Need(await manager.InstallAsync(Decision(preview, [Account], approved: true), Ct));
        var candidate = await CandidateAsync(codeText: "exports.activate = () => 2;", seed: string.Equals(difference, "signer", StringComparison.Ordinal) ? (byte)1 : (byte)0,
            origin: string.Equals(difference, "origin", StringComparison.Ordinal) ? new Uri("https://other.example.org/") : Origin,
            account: string.Equals(difference, "account", StringComparison.Ordinal) ? "other-account" : Account);
        var review = await manager.ReviewManagedCandidateAsync(candidate, Ct);
        Assert.False(review.IsOk);
        var unchanged = Need(await manager.FindAsync(Id, Ct));
        Assert.Equal(installed.Package.Identity, unchanged!.Package.Identity);
        Assert.False(unchanged.Enabled);
    }

    [Fact]
    public async Task A_fresh_review_from_the_same_managed_source_replaces_the_archive_and_stays_disabled()
    {
        var rig = new PackageTestRig();
        using var manager = rig.Manager();
        var original = Need(await manager.ReviewManagedCandidateAsync(await CandidateAsync(), Ct));
        _ = Need(await manager.InstallAsync(Decision(original, [Account], approved: true), Ct));
        var update = Need(await manager.ReviewManagedCandidateAsync(await CandidateAsync(codeText: "exports.activate = () => 3;"), Ct));
        Assert.Equal(original.Candidate.Identity, update.Previous!.Package.Identity);
        var installed = Need(await manager.InstallAsync(Decision(update, [Account], approved: true), Ct));
        Assert.NotEqual(original.Candidate.Identity, installed.Package.Identity);
        Assert.False(installed.Enabled);
        Assert.False(rig.Persistence.HasContent(PackageTestRig.Profile, original.Candidate.Identity));
        Assert.Null(Need(await manager.GetCurrentAsync(Id, Ct)));
    }

    [Fact]
    public async Task ManagedCandidateCannotReplaceAnExistingLocalPackage()
    {
        var rig = new PackageTestRig();
        using var manager = rig.Manager();
        using var local = PackageTestRig.ArchiveForId(Id, "export default 1");
        var localReview = Need(await manager.InspectAsync(local, PluginPackageSource.LocalImport, Ct));
        var localInstallation = Need(await manager.InstallAsync(Decision(localReview, [], approved: false), Ct));
        var candidate = await CandidateAsync();
        var review = await manager.ReviewManagedCandidateAsync(candidate, Ct);
        if (review.IsOk) Assert.False((await manager.InstallAsync(Decision(review.Value!, [Account], approved: true), Ct)).IsOk);
        Assert.Equal(localInstallation.Package.Identity, Need(await manager.FindAsync(Id, Ct))!.Package.Identity);
    }

    private static PluginInstallDecision Decision(PluginInstallPreview preview, string[] accounts, bool approved) =>
        new(preview.ReviewId, preview.Candidate.Identity, preview.Candidate.RequestedPermissions, accounts,
            preview.Candidate.RequestedHttpOrigins, approvePrivilegedJavaScript: approved);

    private static async Task<PluginInstallation> InstallManagedAsync(PluginPackageManager manager, ManagedPluginCandidate candidate)
    {
        var preview = Need(await manager.ReviewManagedCandidateAsync(candidate, Ct));
        return Need(await manager.InstallAsync(Decision(preview, [Account], approved: true), Ct));
    }

    private static async Task<ManagedPluginCandidate> CandidateAsync(string codeText = "exports.activate = () => 1;", byte seed = 0,
        Uri? origin = null, string account = Account, Func<Func<bool>, bool>? current = null, string name = "Managed probe")
    {
        var code = System.Text.Encoding.UTF8.GetBytes(codeText);
        var metadata = new JsonObject { ["plugins"] = new JsonArray(new JsonObject
        {
            ["id"] = Id, ["name"] = name, ["author"] = "Example", ["version"] = "1.0.0", ["type"] = "hook",
            ["tier"] = "privileged", ["dev"] = false, ["entrypoint"] = "scripts/plugin.js", ["bundleHash"] = Convert.ToHexStringLower(SHA256.HashData(code)),
            ["permissions"] = new JsonArray("crypto:full", "email:raw-send"),
        }) };
        using var json = JsonDocument.Parse(metadata.ToJsonString());
        var registry = Need(ManagedPluginRegistry.Parse(json.RootElement));
        Assert.NotNull(registry.FindEligible(Id));
        var keyBytes = new byte[32];
        keyBytes[0] = seed;
        var key = new Ed25519PrivateKeyParameters(keyBytes);
        var signer = new Ed25519Signer();
        signer.Init(true, key);
        signer.BlockUpdate(code, 0, code.Length);
        var artifact = Need(await ManagedPluginArtifactFactory.CreateAsync(registry, Id, code,
            Convert.ToBase64String(key.GeneratePublicKey().GetEncoded()), Convert.ToBase64String(signer.GenerateSignature()), "1.21.2", Ct));
        return new(artifact, origin ?? Origin, account, current ?? (issue => issue()));
    }

    private static T Need<T>(Barbican.Core.Common.Result<T> value)
    {
        Assert.True(value.IsOk, value.Error?.Message);
        return value.Value!;
    }
}
