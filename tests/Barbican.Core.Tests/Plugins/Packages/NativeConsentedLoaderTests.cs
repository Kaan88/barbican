// RED: host-native full-trust activation, runtime manifest parity and live revocation before executable loading.
using Barbican.Core.Common;
using Barbican.Core.Plugins.Native;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Runtime;
namespace Barbican.Core.Tests.Plugins.Packages;

[Collection("Native package sentinel serial")]
public sealed class NativeConsentedLoaderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Red("m6b-native-loader")]
    public async Task ExplicitlyConsentedEnabledPackageActuallyLoadsAndActivatesFixture()
    {
        await WithMarkerAsync(async directory =>
        {
            var (manager, coordinator, binding) = await InstalledAsync(mismatchedHooks: false);
            await using (binding)
            {
                var loader = new ConsentedNativeLoader(manager, manager, coordinator);
                var activation = PluginPackageManagerAcceptanceTests.Need(await loader.LoadAsync(binding, Ct));
                await using (activation)
                {
                    Assert.Equal("sentinel-fixture", activation.Manifest.Id, StringComparer.Ordinal);
                    var activity = await File.ReadAllTextAsync(Path.Combine(directory, "activity"), Ct);
                    Assert.Contains("module", activity, StringComparison.Ordinal);
                    Assert.Contains("constructor", activity, StringComparison.Ordinal);
                    Assert.Contains("manifest", activity, StringComparison.Ordinal);
                }
            }
        });
    }

    [Fact]
    [Red("m6b-native-loader")]
    public async Task SidecarRuntimeManifestMismatchRefusesActivationAfterConsent()
    {
        await WithMarkerAsync(async _ =>
        {
            var (manager, coordinator, binding) = await InstalledAsync(mismatchedHooks: true);
            await using (binding)
            {
                var loader = new ConsentedNativeLoader(manager, manager, coordinator);
                Assert.False((await loader.LoadAsync(binding, Ct)).IsOk);
            }
        });
    }

    [Fact]
    [Red("m6b-native-loader")]
    public async Task RevocationDuringPendingAuthorityCheckPreventsAnyFixtureExecution()
    {
        await WithMarkerAsync(async directory =>
        {
            var (manager, coordinator, binding) = await InstalledAsync(mismatchedHooks: false);
            await using (binding)
            {
                var authority = new BlockingAuthority(manager);
                var loader = new ConsentedNativeLoader(manager, authority, coordinator);
                var pending = loader.LoadAsync(binding, Ct);
                await authority.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
                Assert.True((await manager.SetEnabledAsync(binding.Package, false, Ct)).IsOk);
                authority.Release.TrySetResult();
                Assert.False((await pending).IsOk);
                Assert.False(File.Exists(Path.Combine(directory, "activity")));
            }
        });
    }

    private static async Task<(PluginPackageManager Manager, PluginRevocationCoordinator Coordinator, PluginRuntimeBinding Binding)> InstalledAsync(bool mismatchedHooks)
    {
        var rig = new PackageTestRig();
        var coordinator = new PluginRevocationCoordinator(PackageTestRig.Profile);
        var manager = new PluginPackageManager(new(PackageTestRig.Profile, "1.21.2"), rig.Persistence, coordinator, rig.Time);
        var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "native-sentinel", "NativeSentinel.dll"), Ct);
        using var archive = NativePackagePreloadAcceptanceTests.NativeArchive(bytes, mismatchedHooks: mismatchedHooks);
        var preview = PluginPackageManagerAcceptanceTests.Need(await manager.InspectAsync(archive, PluginPackageSource.LocalImport, Ct));
        var decision = new PluginInstallDecision(preview.ReviewId, preview.Candidate.Identity, [], ["login-a"], approveNativeFullTrust: true);
        var installed = PluginPackageManagerAcceptanceTests.Need(await manager.InstallAsync(decision, Ct));
        Assert.True((await manager.SetEnabledAsync(installed.Package.Identity, true, Ct)).IsOk);
        var grant = PluginPackageManagerAcceptanceTests.Need(await manager.GetCurrentAsync(installed.Package.Identity.Id, Ct))!;
        return (manager, coordinator, PluginRuntimeBinding.Create(PackageTestRig.Profile, grant, PluginFrameKind.Background));
    }

    private static async Task WithMarkerAsync(Func<string, Task> action)
    {
        var directory = NativePackagePreloadAcceptanceTests.MarkerDirectory();
        Assert.False(Directory.Exists(directory));
        Directory.CreateDirectory(directory);
        try { await action(directory); }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private sealed class BlockingAuthority(IPluginAuthority inner) : IPluginAuthority
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public event EventHandler<PluginAuthorityChangedEventArgs>? Changed { add => inner.Changed += value; remove => inner.Changed -= value; }
        public async Task<Result<PluginGrantSnapshot?>> GetCurrentAsync(string pluginId, CancellationToken ct)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(ct);
            return await inner.GetCurrentAsync(pluginId, ct);
        }
    }
}
