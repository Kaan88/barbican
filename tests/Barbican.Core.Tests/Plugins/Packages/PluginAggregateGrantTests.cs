// Synthetic aggregate authority tests for a synchronous issue boundary.
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Runtime;

namespace Barbican.Core.Tests.Plugins.Packages;

public sealed class PluginAggregateGrantTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AllContributorsMustRemainGrantedBeforeIssue()
    {
        var rig = new PackageTestRig();
        using var manager = rig.LeasedManager();
        var first = await InstallAsync(manager, "first-plugin");
        var second = await InstallAsync(manager, "second-plugin");
        await using var one = PluginRuntimeBinding.Create(PackageTestRig.Profile, first, PluginFrameKind.Background);
        await using var two = PluginRuntimeBinding.Create(PackageTestRig.Profile, second, PluginFrameKind.Background);
        var issued = 0;
        Assert.True(await manager.TryRunWithGrantsAsync([one, two], () => { issued++; return true; }, Ct));
        Assert.Equal(1, issued);
        Assert.True((await manager.SetEnabledAsync(second.Package, false, Ct)).IsOk);
        Assert.False(await manager.TryRunWithGrantsAsync([one, two], () => { issued++; return true; }, Ct));
        Assert.Equal(1, issued);
        Assert.True(await manager.TryRunWithGrantsAsync([one], () => { issued++; return true; }, Ct));
        Assert.Equal(2, issued);
    }

    [Fact]
    public async Task ForeignProfileAndEmptyContributorSetCannotIssue()
    {
        var rig = new PackageTestRig();
        using var manager = rig.LeasedManager();
        var grant = await InstallAsync(manager, "only-plugin");
        await using var foreign = PluginRuntimeBinding.Create("other-profile", grant, PluginFrameKind.Background);
        Assert.False(await manager.TryRunWithGrantsAsync([], () => throw new InvalidOperationException("Empty set issued."), Ct));
        Assert.False(await manager.TryRunWithGrantsAsync([foreign], () => throw new InvalidOperationException("Foreign profile issued."), Ct));
    }

    private static async Task<Barbican.Core.Plugins.Permissions.PluginGrantSnapshot> InstallAsync(PluginPackageManager manager, string id)
    {
        using var archive = PackageTestRig.ArchiveForId(id, "export default 1", "email:send");
        var preview = PluginPackageManagerAcceptanceTests.Need(await manager.InspectAsync(archive, PluginPackageSource.LocalImport, Ct));
        var installed = PluginPackageManagerAcceptanceTests.Need(await manager.InstallAsync(PluginPackageManagerAcceptanceTests.Decision(preview, ["email:send"], ["login-a"]), Ct));
        Assert.True((await manager.SetEnabledAsync(installed.Package.Identity, true, Ct)).IsOk);
        return PluginPackageManagerAcceptanceTests.Need(await manager.GetCurrentAsync(id, Ct))!;
    }
}
