// M6b host-added exclusive profile authority; tests use only unique owned temporary folders.
using Barbican.Core.Plugins.Persistence;
using Barbican.Core.Storage;
namespace Barbican.Core.Tests.Plugins.Packages;

public sealed class PluginProfileAuthorityLeaseTests
{
    [Fact]
    public async Task SameProfileRootCannotBeOwnedTwiceAndDisposalCancelsLifetime()
    {
        var root = Path.Combine(Path.GetTempPath(), "barbican-lease-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var paths = ProfilePaths.At("agent-lease", root, root);
            var factory = new PluginProfileAuthorityLeaseFactory();
            var first = PluginPackageManagerAcceptanceTests.Need(await factory.AcquireAsync(paths, TestContext.Current.CancellationToken));
            var token = first.Lifetime;
            Assert.True(first.IsHeld);
            Assert.False((await factory.AcquireAsync(paths, TestContext.Current.CancellationToken)).IsOk);
            await first.DisposeAsync();
            Assert.False(first.IsHeld);
            Assert.True(token.IsCancellationRequested);
            Assert.True(first.Lifetime.IsCancellationRequested);
            var next = PluginPackageManagerAcceptanceTests.Need(await factory.AcquireAsync(paths, TestContext.Current.CancellationToken));
            await next.DisposeAsync();
        }
        finally { Directory.Delete(root, recursive: true); }
    }
    [Fact]
    public async Task LeaseRemainsHeldUntilAnAdmittedWriteFinishes()
    {
        var ct = TestContext.Current.CancellationToken;
        var root = Directory.CreateTempSubdirectory("barbican-post-lease-");
        try
        {
            var paths = ProfilePaths.At("agent-lease", root.FullName, root.FullName);
            await using var lease = PluginPackageManagerAcceptanceTests.Need(
                await new PluginProfileAuthorityLeaseFactory().AcquireAsync(paths, ct));
            await VerifyLeasePostAsync(lease, ct);
        }
        finally { root.Delete(recursive: true); }
    }

    private static async Task VerifyLeasePostAsync(IPluginProfileAuthorityLease lease, CancellationToken ct)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var write = Task.Run(() => lease.TryRunWhileHeld(() =>
        {
            entered.SetResult();
            return release.Wait(TimeSpan.FromSeconds(5)) && lease.IsHeld;
        }), ct);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        var close = Task.Run(async () =>
        {
            closing.SetResult();
            await lease.DisposeAsync();
        }, ct);
        try
        {
            await closing.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            Assert.False(close.IsCompleted);
        }
        finally { release.Set(); }

        Assert.True(await write);
        await close;
        Assert.False(lease.TryRunWhileHeld(() => throw new InvalidOperationException("Released lease posted.")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LeasedManagerCachesAuthorityAndShutdownRevokesItsTrackedRuntime(bool managerFirst)
    {
        var root = Path.Combine(Path.GetTempPath(), "barbican-lease-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var rig = new PackageTestRig();
            var factory = new PluginProfileAuthorityLeaseFactory();
            await using var lease = PluginPackageManagerAcceptanceTests.Need(await factory.AcquireAsync(ProfilePaths.At(PackageTestRig.Profile, root, root), TestContext.Current.CancellationToken));
            var coordinator = new Barbican.Core.Plugins.Runtime.PluginRevocationCoordinator(PackageTestRig.Profile);
            using var manager = new Barbican.Core.Plugins.Packages.PluginPackageManager(new(PackageTestRig.Profile, "1.21.2"), rig.Persistence, coordinator, rig.Time, lease);
            using var archive = PackageTestRig.Archive("export default 1", "email:read");
            var preview = PluginPackageManagerAcceptanceTests.Need(await manager.InspectAsync(archive, Barbican.Core.Plugins.Packages.PluginPackageSource.LocalImport, TestContext.Current.CancellationToken));
            var installed = PluginPackageManagerAcceptanceTests.Need(await manager.InstallAsync(PluginPackageManagerAcceptanceTests.Decision(preview, ["email:read"], ["login-a"]), TestContext.Current.CancellationToken));
            Assert.True((await manager.SetEnabledAsync(installed.Package.Identity, true, TestContext.Current.CancellationToken)).IsOk);
            var grant = PluginPackageManagerAcceptanceTests.Need(await manager.GetCurrentAsync(PackageTestRig.PluginId, TestContext.Current.CancellationToken))!;
            var reads = rig.Persistence.Loads;
            Assert.NotNull(PluginPackageManagerAcceptanceTests.Need(await manager.GetCurrentAsync(PackageTestRig.PluginId, TestContext.Current.CancellationToken)));
            Assert.Equal(reads, rig.Persistence.Loads);
            await using var binding = Barbican.Core.Plugins.Runtime.PluginRuntimeBinding.Create(PackageTestRig.Profile, grant, Barbican.Core.Plugins.Runtime.PluginFrameKind.Background);
            var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = PluginPackageManagerAcceptanceTests.Need(coordinator.Track(binding, _ => { stopped.TrySetResult(); return ValueTask.CompletedTask; }));
            if (managerFirst)
            {
                manager.Dispose();
                Assert.False(binding.IsAlive);
            }

            await lease.DisposeAsync();
            var unavailable = await manager.GetCurrentAsync(PackageTestRig.PluginId, TestContext.Current.CancellationToken);
            Assert.False(unavailable.IsOk);
            await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.False(binding.IsAlive);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
    [Fact]
    public async Task LeaseLossDuringStoreLoadCannotPublishAReview()
    {
        var root = Path.Combine(Path.GetTempPath(), "barbican-lease-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var rig = new PackageTestRig();
            var lease = PluginPackageManagerAcceptanceTests.Need(await new PluginProfileAuthorityLeaseFactory().AcquireAsync(ProfilePaths.At(PackageTestRig.Profile, root, root), TestContext.Current.CancellationToken));
            using var manager = new Barbican.Core.Plugins.Packages.PluginPackageManager(new(PackageTestRig.Profile, "1.21.2"), rig.Persistence, rig.Cleanup, rig.Time, lease);
            rig.Persistence.LoadEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            rig.Persistence.ReleaseLoad = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using var archive = PackageTestRig.Archive();
            var pending = manager.InspectAsync(archive, Barbican.Core.Plugins.Packages.PluginPackageSource.LocalImport, TestContext.Current.CancellationToken);
            await rig.Persistence.LoadEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await lease.DisposeAsync();
            rig.Persistence.ReleaseLoad.TrySetResult();
            Assert.False((await pending).IsOk);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
