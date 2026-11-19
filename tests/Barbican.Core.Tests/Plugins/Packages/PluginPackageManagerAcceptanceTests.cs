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
