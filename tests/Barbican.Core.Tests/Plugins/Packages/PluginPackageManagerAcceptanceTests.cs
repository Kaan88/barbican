// RED acceptance for the real PluginPackageManager; Bulwark stores/plugin-store.ts:188-320 and M6b package authority.
using Barbican.Core.Common;
using Barbican.Core.Plugins.Packages;

namespace Barbican.Core.Tests.Plugins.Packages;

public sealed class PluginPackageManagerAcceptanceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ImportConsentInstallEnableDisableUninstallUsesLiveAuthority()
    {
        var rig = new PackageTestRig();
        var manager = rig.Manager();
        using var archive = PackageTestRig.Archive("export default 1", "email:read");
        var preview = Need(await manager.InspectAsync(archive, PluginPackageSource.LocalImport, Ct));
        Assert.NotEmpty(preview.ReviewId);
        var installed = Need(await manager.InstallAsync(Decision(preview, ["email:read"], ["login-a"]), Ct));
        Assert.False(installed.Enabled);
        Assert.Equal(preview.Candidate.Identity, installed.Package.Identity);
        Assert.True((await manager.SetEnabledAsync(installed.Package.Identity, true, Ct)).IsOk);
        var live = Need(await manager.GetCurrentAsync(PackageTestRig.PluginId, Ct));
        Assert.NotNull(live);
        Assert.True(live.Enabled);
        Assert.True(live.Authorizes(installed.Package.Identity, live.Generation, "email:read", "login-a"));
        Assert.False(live.Authorizes(installed.Package.Identity, live.Generation, "email:read", "login-b"));
        Assert.True((await manager.SetEnabledAsync(installed.Package.Identity, false, Ct)).IsOk);
        Assert.True(rig.Cleanup.Calls > 0);
        var stopped = await manager.GetCurrentAsync(PackageTestRig.PluginId, Ct);
        Assert.True(stopped.IsOk);
        Assert.True(stopped.Value is null || !stopped.Value.Enabled);
        Assert.True((await manager.UninstallAsync(installed.Package.Identity, Ct)).IsOk);
        Assert.Null(Need(await manager.FindAsync(PackageTestRig.PluginId, Ct)));
    }

    [Fact]
    public async Task InstallingPastThePackageLimitKeepsTheExistingIndexUsable()
    {
        var rig = new PackageTestRig();
        var entries = new List<PluginPackageManager.Entry>();
        for (var i = 1; i <= 1000; i++)
        {
            var id = "fixture-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            using var archive = PackageTestRig.ArchiveForId(id, "export default 1");
            var bytes = archive.ToArray();
            var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
            var identity = new PluginPackageIdentity(id, "1.0.0", hash);
            Assert.True((await rig.Persistence.PutContentAsync(PackageTestRig.Profile, identity, bytes, Ct)).IsOk);
            entries.Add(new(id, identity.Version, hash, false, i, [], []));
        }

        var index = new PluginPackageManager.Index(1001, entries);
        var data = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(index, PluginPackageManager.PackageIndexJsonContext.Default.Index);
        Assert.True((await rig.Persistence.CommitAsync(PackageTestRig.Profile, 0, data, Ct)).IsOk);
        using var manager = rig.Manager();
        using var extra = PackageTestRig.ArchiveForId("fixture-extra", "export default 1");
        var preview = Need(await manager.InspectAsync(extra, PluginPackageSource.LocalImport, Ct));

        Assert.False((await manager.InstallAsync(Decision(preview, [], []), Ct)).IsOk);
        Assert.False(rig.Persistence.HasContent(PackageTestRig.Profile, preview.Candidate.Identity));
        Assert.Equal(1000, Need(await manager.ListAsync(Ct)).Count);
    }

    [Fact]
    public async Task FailedUninstallArchiveDeletionCanBeRetriedAfterRestart()
    {
        var rig = new PackageTestRig();
        using var manager = rig.Manager();
        using var archive = PackageTestRig.Archive();
        var preview = Need(await manager.InspectAsync(archive, PluginPackageSource.LocalImport, Ct));
        var installed = Need(await manager.InstallAsync(Decision(preview, [], []), Ct));
        rig.Persistence.FailDelete = true;

        Assert.False((await manager.UninstallAsync(installed.Package.Identity, Ct)).IsOk);
        Assert.Empty(Need(await manager.ListAsync(Ct)));
        Assert.True(rig.Persistence.HasContent(PackageTestRig.Profile, installed.Package.Identity));

        rig.Persistence.FailDelete = false;
        using var reopened = rig.Manager();
        var wrong = new PluginPackageIdentity("other-package", installed.Package.Identity.Version, installed.Package.Identity.Sha256);
        Assert.False((await reopened.UninstallAsync(wrong, Ct)).IsOk);
        Assert.True(rig.Persistence.HasContent(PackageTestRig.Profile, installed.Package.Identity));
        Assert.True((await reopened.UninstallAsync(installed.Package.Identity, Ct)).IsOk);
        Assert.False(rig.Persistence.HasContent(PackageTestRig.Profile, installed.Package.Identity));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReplacingAPackageRetiresOnlyThePreviousArchive(bool failDelete)
    {
        var rig = new PackageTestRig();
        using var manager = rig.Manager();
        using var previous = PackageTestRig.Archive("export default 1");
        using var replacement = PackageTestRig.Archive("export default 2");
        var oldReview = Need(await manager.InspectAsync(previous, PluginPackageSource.LocalImport, Ct));
        var old = Need(await manager.InstallAsync(Decision(oldReview, [], []), Ct)).Package.Identity;
        var newReview = Need(await manager.InspectAsync(replacement, PluginPackageSource.LocalImport, Ct));
        rig.Persistence.FailDelete = failDelete;
        var current = Need(await manager.InstallAsync(Decision(newReview, [], []), Ct)).Package.Identity;
        Assert.Equal(failDelete, rig.Persistence.HasContent(PackageTestRig.Profile, old));

        rig.Persistence.FailDelete = false;
        using var reopened = rig.Manager();
        Assert.Single(Need(await reopened.ListAsync(Ct)));
        Assert.False(rig.Persistence.HasContent(PackageTestRig.Profile, old));
        Assert.True(rig.Persistence.HasContent(PackageTestRig.Profile, current));
    }

    [Fact]
    public async Task ReinstallCancelsArchiveRetirementBeforeContentCanBeDeleted()
    {
        var rig = new PackageTestRig();
        using var initial = rig.Manager();
        using var archive = PackageTestRig.Archive();
        var preview = Need(await initial.InspectAsync(archive, PluginPackageSource.LocalImport, Ct));
        var identity = Need(await initial.InstallAsync(Decision(preview, [], []), Ct)).Package.Identity;
        rig.Persistence.FailDelete = true;
        Assert.False((await initial.UninstallAsync(identity, Ct)).IsOk);

        rig.Persistence.FailDelete = false;
        using var reopened = rig.Manager();
        archive.Position = 0;
        var reinstall = Need(await reopened.InspectAsync(archive, PluginPackageSource.LocalImport, Ct));
        Assert.Equal(identity, Need(await reopened.InstallAsync(Decision(reinstall, [], []), Ct)).Package.Identity);
        Assert.True((await reopened.SetEnabledAsync(identity, true, Ct)).IsOk);
        Assert.Single(Need(await reopened.ListAsync(Ct)));
        Assert.NotNull(Need(await reopened.GetCurrentAsync(identity.Id, Ct)));
        Assert.True(rig.Persistence.HasContent(PackageTestRig.Profile, identity));
        Assert.Equal(1, rig.Persistence.Deletes);
    }

    [Fact]
    public async Task RejectedInstallCommitLeavesAnArchiveThatTheNextListCanRemove()
    {
        var rig = new PackageTestRig();
        using var manager = rig.Manager();
        using var archive = PackageTestRig.Archive();
        var preview = Need(await manager.InspectAsync(archive, PluginPackageSource.LocalImport, Ct));
        rig.Persistence.FailCommitAtRevision = 1;

        Assert.False((await manager.InstallAsync(Decision(preview, [], []), Ct)).IsOk);
        Assert.True(rig.Persistence.HasContent(PackageTestRig.Profile, preview.Candidate.Identity));
        Assert.Null(Need(await manager.GetCurrentAsync(preview.Candidate.Identity.Id, Ct)));

        rig.Persistence.FailCommitAtRevision = null;
        using var reopened = rig.Manager();
        Assert.Empty(Need(await reopened.ListAsync(Ct)));
        Assert.False(rig.Persistence.HasContent(PackageTestRig.Profile, preview.Candidate.Identity));
    }

    [Fact]
    public async Task GenerationCapacityAlwaysLeavesRoomToRevokeAnEnabledPackage()
    {
        var rig = new PackageTestRig();
        using var archive = PackageTestRig.Archive();
        var bytes = archive.ToArray();
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
        var identity = new PluginPackageIdentity(PackageTestRig.PluginId, "1.0.0", hash);
        Assert.True((await rig.Persistence.PutContentAsync(PackageTestRig.Profile, identity, bytes, Ct)).IsOk);
        var index = new PluginPackageManager.Index(long.MaxValue - 2, [new(identity.Id, identity.Version, hash, true, 5, [], [])]);
        var data = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(index, PluginPackageManager.PackageIndexJsonContext.Default.Index);
        Assert.True((await rig.Persistence.CommitAsync(PackageTestRig.Profile, 0, data, Ct)).IsOk);

        using var manager = rig.Manager();
        Assert.NotNull(Need(await manager.GetCurrentAsync(identity.Id, Ct)));
        Assert.True((await manager.SetEnabledAsync(identity, false, Ct)).IsOk);
        using var reopened = rig.Manager();
        Assert.Null(Need(await reopened.GetCurrentAsync(identity.Id, Ct)));
        Assert.False((await reopened.SetEnabledAsync(identity, true, Ct)).IsOk);
        Assert.True((await reopened.UninstallAsync(identity, Ct)).IsOk);
    }

    [Theory]
    [InlineData(7, 5)]
    [InlineData(7, 6)]
    [InlineData(long.MaxValue, 1)]
    public async Task InvalidAuthenticatedGenerationsFailClosed(long nextGeneration, long cleanupThrough)
    {
        var rig = new PackageTestRig();
        using var archive = PackageTestRig.Archive();
        var bytes = archive.ToArray();
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
        var identity = new PluginPackageIdentity(PackageTestRig.PluginId, "1.0.0", hash);
        Assert.True((await rig.Persistence.PutContentAsync(PackageTestRig.Profile, identity, bytes, Ct)).IsOk);
        var index = new PluginPackageManager.Index(nextGeneration, [new(identity.Id, identity.Version, hash, false, 5, [], [], cleanupThrough)]);
        var data = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(index, PluginPackageManager.PackageIndexJsonContext.Default.Index);
        Assert.True((await rig.Persistence.CommitAsync(PackageTestRig.Profile, 0, data, Ct)).IsOk);

        using var manager = rig.Manager();
        Assert.False((await manager.ListAsync(Ct)).IsOk);
    }

    [Fact]
    public async Task AuthorityChangeObserverMayReadCurrentAuthoritySynchronously()
    {
        var rig = new PackageTestRig();
        var manager = rig.Manager();
        using var archive = PackageTestRig.Archive("export default 1", "email:read");
        var preview = Need(await manager.InspectAsync(archive, PluginPackageSource.LocalImport, Ct));
        var observed = false;
#pragma warning disable VSTHRD002 // Deliberate synchronous reentry tests that observers are called outside the gate.
        manager.Changed += (_, _) => observed = manager.GetCurrentAsync(PackageTestRig.PluginId, CancellationToken.None).GetAwaiter().GetResult().IsOk;
#pragma warning restore VSTHRD002
        var operation = Task.Run(() => manager.InstallAsync(Decision(preview, ["email:read"], ["login-a"]), CancellationToken.None), CancellationToken.None);
        Assert.True((await operation.WaitAsync(TimeSpan.FromSeconds(5), Ct)).IsOk);
        Assert.True(observed);
    }

    [Fact]
    public async Task ReviewTokenIsOneUseExpiresAndCannotApproveAnotherHash()
    {
        var rig = new PackageTestRig();
        var manager = rig.Manager();
        using var first = PackageTestRig.Archive("export default 1", "email:read");
        using var second = PackageTestRig.Archive("export default 2", "email:read");
        var a = Need(await manager.InspectAsync(first, PluginPackageSource.LocalImport, Ct));
        var b = Need(await manager.InspectAsync(second, PluginPackageSource.LocalImport, Ct));
        Assert.NotEqual(a.Candidate.Identity.Sha256, b.Candidate.Identity.Sha256, StringComparer.Ordinal);
        Assert.False((await manager.InstallAsync(new PluginInstallDecision(a.ReviewId, b.Candidate.Identity, ["email:read"], ["login-a"]), Ct)).IsOk);
        Assert.True((await manager.InstallAsync(Decision(a, ["email:read"], ["login-a"]), Ct)).IsOk);
        Assert.False((await manager.InstallAsync(Decision(a, ["email:read"], ["login-a"]), Ct)).IsOk);
        rig.Time.Advance(TimeSpan.FromMinutes(16));
        Assert.False((await manager.InstallAsync(Decision(b, ["email:read"], ["login-a"]), Ct)).IsOk);
    }

    [Fact]
    public async Task ReviewsAreBoundedAndAnOldReviewCannotReplaceNewerContent()
    {
        var rig = new PackageTestRig();
        var manager = rig.Manager();
        using var original = PackageTestRig.Archive("export default 1", "email:read");
        var first = Need(await manager.InspectAsync(original, PluginPackageSource.LocalImport, Ct));
        var installed = Need(await manager.InstallAsync(Decision(first, ["email:read"], ["login-a"]), Ct));
        var stale = Need(await manager.ReviewAsync(installed.Package.Identity, Ct));
        using var changed = PackageTestRig.Archive("export default 2", "email:read");
        var next = Need(await manager.InspectAsync(changed, PluginPackageSource.LocalImport, Ct));
        Assert.True((await manager.InstallAsync(Decision(next, ["email:read"], ["login-a"]), Ct)).IsOk);
        Assert.False((await manager.InstallAsync(Decision(stale, ["email:read"], ["login-a"]), Ct)).IsOk);
        for (var i = 0; i < 8; i++)
        {
            using var pending = PackageTestRig.Archive("export default " + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Assert.True((await manager.InspectAsync(pending, PluginPackageSource.LocalImport, Ct)).IsOk);
        }
        using var overLimit = PackageTestRig.Archive();
        Assert.False((await manager.InspectAsync(overLimit, PluginPackageSource.LocalImport, Ct)).IsOk);
    }

    [Fact]
    public async Task MalformedAuthenticatedIndexDoesNotBecomeAnEnabledGrant()
    {
        var rig = new PackageTestRig();
        var manager = rig.Manager();
        using var archive = PackageTestRig.Archive("export default 1", "email:read");
        var preview = Need(await manager.InspectAsync(archive, PluginPackageSource.LocalImport, Ct));
        var installed = Need(await manager.InstallAsync(Decision(preview, ["email:read"], ["login-a"]), Ct));
        Assert.True((await manager.SetEnabledAsync(installed.Package.Identity, true, Ct)).IsOk);
        rig.Persistence.CorruptIndex(PackageTestRig.Profile);
        var current = await rig.Manager().GetCurrentAsync(PackageTestRig.PluginId, Ct);
        Assert.True(!current.IsOk || current.Value is null || !current.Value.Enabled);
    }

    [Fact]
    public async Task PermissionExpansionAndSameVersionNewCodeRequireNewExactConsent()
    {
        var rig = new PackageTestRig();
        var manager = rig.Manager();
        using var one = PackageTestRig.Archive("export default 1", "email:read");
        var old = Need(await manager.InspectAsync(one, PluginPackageSource.LocalImport, Ct));
        var installed = Need(await manager.InstallAsync(Decision(old, ["email:read"], ["login-a"]), Ct));
        Assert.True((await manager.SetEnabledAsync(installed.Package.Identity, true, Ct)).IsOk);
        var oldGrant = Need(await manager.GetCurrentAsync(PackageTestRig.PluginId, Ct))!;
        using var update = PackageTestRig.Archive("export default 2", "email:read", "email:write");
        var next = Need(await manager.InspectAsync(update, PluginPackageSource.LocalImport, Ct));
        Assert.Equal(old.Candidate.Identity.Version, next.Candidate.Identity.Version, StringComparer.Ordinal);
        Assert.NotEqual(old.Candidate.Identity.Sha256, next.Candidate.Identity.Sha256, StringComparer.Ordinal);
        Assert.False((await manager.InstallAsync(Decision(next, ["email:read"], ["login-a"]), Ct)).IsOk);
        var updated = Need(await manager.InstallAsync(Decision(next, ["email:read", "email:write"], ["login-a"]), Ct));
        Assert.False(updated.Enabled);
        var current = await manager.GetCurrentAsync(PackageTestRig.PluginId, Ct);
        Assert.True(current.IsOk);
        Assert.True(current.Value is null || !current.Value.Authorizes(oldGrant.Package, oldGrant.Generation, "email:read", "login-a"));
    }

    [Fact]
    public async Task CallerSuppliedSourceCannotForgeTrustedProvenance()
    {
        var manager = new PackageTestRig().Manager();
        foreach (var source in new[] { PluginPackageSource.Managed, PluginPackageSource.Bundled, PluginPackageSource.Registry })
        {
            using var archive = PackageTestRig.Archive();
            Assert.False((await manager.InspectAsync(archive, source, Ct)).IsOk);
        }
    }

    [Fact]
    public async Task PendingCleanupBlocksUninstallAndReplacementWithoutDeletingContent()
    {
        var rig = new PackageTestRig();
        var manager = rig.Manager();
        using var original = PackageTestRig.Archive("export default 1", "email:read");
        var first = Need(await manager.InspectAsync(original, PluginPackageSource.LocalImport, Ct));
        var installed = Need(await manager.InstallAsync(Decision(first, ["email:read"], ["login-a"]), Ct));
        Assert.True((await manager.SetEnabledAsync(installed.Package.Identity, true, Ct)).IsOk);
        rig.Cleanup.Pending = true;
        Assert.False((await manager.SetEnabledAsync(installed.Package.Identity, false, Ct)).IsOk);
        Assert.False((await rig.Manager().SetEnabledAsync(installed.Package.Identity, true, Ct)).IsOk);
        var grant = await manager.GetCurrentAsync(PackageTestRig.PluginId, Ct);
        Assert.True(!grant.IsOk || grant.Value is null || !grant.Value.Enabled);
        using var replacement = PackageTestRig.Archive("export default 2", "email:read");
        var next = Need(await manager.InspectAsync(replacement, PluginPackageSource.LocalImport, Ct));
        Assert.False((await manager.InstallAsync(Decision(next, ["email:read"], ["login-a"]), Ct)).IsOk);
        Assert.False((await manager.UninstallAsync(installed.Package.Identity, Ct)).IsOk);
        Assert.True(rig.Persistence.HasContent(PackageTestRig.Profile, installed.Package.Identity));
        Assert.Equal(0, rig.Persistence.Deletes);
    }

    [Fact]
    public async Task CorruptAuthenticatedIndexOrPackageBytesNeverReviveAuthorityAfterRestart()
    {
        var rig = new PackageTestRig();
        var manager = rig.Manager();
        using var archive = PackageTestRig.Archive("export default 1", "email:read");
        var preview = Need(await manager.InspectAsync(archive, PluginPackageSource.LocalImport, Ct));
        var installed = Need(await manager.InstallAsync(Decision(preview, ["email:read"], ["login-a"]), Ct));
        Assert.True((await manager.SetEnabledAsync(installed.Package.Identity, true, Ct)).IsOk);
        rig.Persistence.BadAuthentication = true;
        var reopened = rig.Manager();
        var corrupted = await reopened.GetCurrentAsync(PackageTestRig.PluginId, Ct);
        Assert.True(!corrupted.IsOk || corrupted.Value is null || !corrupted.Value.Enabled);
        rig.Persistence.BadAuthentication = false;
        rig.Persistence.TamperContent(PackageTestRig.Profile, installed.Package.Identity);
        var reloaded = rig.Manager();
        var result = await reloaded.GetCurrentAsync(PackageTestRig.PluginId, Ct);
        Assert.True(!result.IsOk || result.Value is null || !result.Value.Enabled);
        Assert.False((await reloaded.OpenResourceAsync(installed.Package.Identity, "index.js", Ct)).IsOk);
    }

    [Fact]
    public Task CommitFailureNeverExposesNewEnabledAuthority() => CommitFailureAsync(false);

    [Fact]
    public Task AmbiguousCommitNeverExposesNewEnabledAuthority() => CommitFailureAsync(true);

    [Fact]
    public async Task RevokeClosesBindingsEvenWhenDurableDisableFailsAndTeardownMayReenter()
    {
        var rig = new PackageTestRig();
        var manager = rig.Manager();
        using var archive = PackageTestRig.Archive("export default 1", "email:read");
        var preview = Need(await manager.InspectAsync(archive, PluginPackageSource.LocalImport, Ct));
        var installed = Need(await manager.InstallAsync(Decision(preview, ["email:read"], ["login-a"]), Ct));
        Assert.True((await manager.SetEnabledAsync(installed.Package.Identity, true, Ct)).IsOk);
        rig.Persistence.FailBeforeCommit = true;
        var reentered = false;
        rig.Cleanup.OnRevoke = async () => { var state = await manager.GetCurrentAsync(PackageTestRig.PluginId, CancellationToken.None); reentered = state.IsOk && state.Value is null; };
        var operation = manager.SetEnabledAsync(installed.Package.Identity, false, Ct);
        Assert.False((await operation.WaitAsync(TimeSpan.FromSeconds(5), Ct)).IsOk);
        Assert.True(reentered);
        Assert.Equal(1, rig.Cleanup.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedDisableInvalidatesARealTrackedRuntimeBeforeReturningFailure(bool throws)
    {
        var rig = new PackageTestRig();
        var coordinator = new Barbican.Core.Plugins.Runtime.PluginRevocationCoordinator(PackageTestRig.Profile);
        var manager = new PluginPackageManager(new(PackageTestRig.Profile, "1.21.2"), rig.Persistence, coordinator, rig.Time);
        using var archive = PackageTestRig.Archive("export default 1", "email:read");
        var preview = Need(await manager.InspectAsync(archive, PluginPackageSource.LocalImport, Ct));
        var installed = Need(await manager.InstallAsync(Decision(preview, ["email:read"], ["login-a"]), Ct));
        Assert.True((await manager.SetEnabledAsync(installed.Package.Identity, true, Ct)).IsOk);
        var grant = Need(await manager.GetCurrentAsync(PackageTestRig.PluginId, Ct))!;
        await using var binding = Barbican.Core.Plugins.Runtime.PluginRuntimeBinding.Create(PackageTestRig.Profile, grant, Barbican.Core.Plugins.Runtime.PluginFrameKind.Background);
        using var registration = Need(coordinator.Track(binding, _ => ValueTask.CompletedTask));
        rig.Persistence.FailBeforeCommit = !throws;
        rig.Persistence.ThrowOnCommit = throws;
        Assert.False((await manager.SetEnabledAsync(installed.Package.Identity, false, Ct)).IsOk);
        Assert.False(binding.IsAlive);
    }

    [Fact]
    public async Task SameHashReviewCannotRestoreAnOlderGrantGeneration()
    {
        var rig = new PackageTestRig();
        var manager = rig.Manager();
        using var archive = PackageTestRig.Archive("export default 1", "email:read");
        var preview = Need(await manager.InspectAsync(archive, PluginPackageSource.LocalImport, Ct));
        var installed = Need(await manager.InstallAsync(Decision(preview, ["email:read"], ["login-a"]), Ct));
        var oldReview = Need(await manager.ReviewAsync(installed.Package.Identity, Ct));
        Assert.True((await manager.SetEnabledAsync(installed.Package.Identity, true, Ct)).IsOk);
        Assert.False((await manager.InstallAsync(Decision(oldReview, ["email:read"], ["login-a"]), Ct)).IsOk);
        Assert.True(Need(await manager.GetCurrentAsync(PackageTestRig.PluginId, Ct))!.Enabled);
    }

    [Fact]
    public async Task PrivilegedManifestIsVisibleButCannotBeInstalledByLocalConsentAlone()
    {
        var manager = new PackageTestRig().Manager();
        var manifest = "{\"id\":\"fixture-plugin\",\"name\":\"Fixture\",\"version\":\"1.0.0\",\"author\":\"Example\",\"type\":\"hook\",\"tier\":\"privileged\",\"permissions\":[\"crypto:full\"]}";
        using var archive = PackageTestRig.Zip(("manifest.json", System.Text.Encoding.UTF8.GetBytes(manifest)), ("index.js", System.Text.Encoding.UTF8.GetBytes("export default 1")));
        var preview = Need(await manager.InspectAsync(archive, PluginPackageSource.LocalImport, Ct));
        Assert.Equal(Barbican.Core.Plugins.Permissions.PluginExecutionTier.Privileged, preview.Candidate.RequestedTier);
        var decision = new PluginInstallDecision(preview.ReviewId, preview.Candidate.Identity, ["crypto:full"], ["login-a"], approvePrivilegedJavaScript: true);
        Assert.False((await manager.InstallAsync(decision, Ct)).IsOk);
    }

    private static async Task CommitFailureAsync(bool afterCommit)
    {
        var rig = new PackageTestRig();
        var manager = rig.Manager();
        using var original = PackageTestRig.Archive("export default 1", "email:read");
        var first = Need(await manager.InspectAsync(original, PluginPackageSource.LocalImport, Ct));
        var old = Need(await manager.InstallAsync(Decision(first, ["email:read"], ["login-a"]), Ct));
        Assert.True((await manager.SetEnabledAsync(old.Package.Identity, true, Ct)).IsOk);
        using var replacement = PackageTestRig.Archive("export default 2", "email:read");
        var next = Need(await manager.InspectAsync(replacement, PluginPackageSource.LocalImport, Ct));
        rig.Persistence.FailAfterCommit = afterCommit;
        rig.Persistence.FailBeforeCommit = !afterCommit;
        var readsBefore = rig.Persistence.Loads;
        var result = await manager.InstallAsync(Decision(next, ["email:read"], ["login-a"]), Ct);
        if (!afterCommit) Assert.False(result.IsOk);
        if (result.IsOk)
        {
            Assert.True(afterCommit);
            Assert.True(rig.Persistence.Loads > readsBefore);
            Assert.Equal(next.Candidate.Identity, result.Value!.Package.Identity);
            Assert.False(result.Value.Enabled);
        }
        if (!result.IsOk)
        {
            var live = await manager.GetCurrentAsync(PackageTestRig.PluginId, Ct);
            Assert.True(!live.IsOk || live.Value is null || !live.Value.Enabled || live.Value.Package != next.Candidate.Identity);
            Assert.True(rig.Persistence.HasContent(PackageTestRig.Profile, old.Package.Identity));
        }
        rig.Persistence.FailAfterCommit = false;
        rig.Persistence.FailBeforeCommit = false;
        var reopened = rig.Manager();
        var reopenedGrant = await reopened.GetCurrentAsync(PackageTestRig.PluginId, Ct);
        Assert.True(!reopenedGrant.IsOk || reopenedGrant.Value is null || !reopenedGrant.Value.Enabled || reopenedGrant.Value.Package != next.Candidate.Identity);
    }

    internal static PluginInstallDecision Decision(PluginInstallPreview preview, string[] permissions, string[] accounts) => new(preview.ReviewId, preview.Candidate.Identity, permissions, accounts);
    internal static T Need<T>(Result<T> result) { Assert.True(result.IsOk, result.Error?.Message); return result.Value!; }
}
