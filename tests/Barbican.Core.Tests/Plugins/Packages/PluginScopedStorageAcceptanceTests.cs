// Acceptance for the real PluginScopedStorage and PluginPackageManager; Bulwark host-api.ts:407-447 and M6b account-routing.
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;
using Barbican.Core.Plugins.Persistence;

namespace Barbican.Core.Tests.Plugins.Packages;

public sealed class PluginScopedStorageAcceptanceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task StorageIsIsolatedByProfilePluginAndCapturedLogin()
    {
        var rig = new PackageTestRig();
        var (manager, storage, binding, a) = await StartAsync(rig);
        Assert.True((await storage.SetAsync(binding, a, "note", PackageTestRig.StringValue("private-a"), Ct)).IsOk);
        var b = PluginPackageManagerAcceptanceTests.Need(rig.Accounts.CaptureOwner("login-b"));
        Assert.Null(PluginPackageManagerAcceptanceTests.Need(await storage.GetAsync(binding, b, "note", Ct)));
        Assert.Equal("private-a", PluginPackageManagerAcceptanceTests.Need(await storage.GetAsync(binding, a, "note", Ct))!.StringValue, StringComparer.Ordinal);
        using var separatePlugin = PackageTestRig.ArchiveForId("other-plugin", "export default 1", "email:read");
        var separateReview = PluginPackageManagerAcceptanceTests.Need(await manager.InspectAsync(separatePlugin, PluginPackageSource.LocalImport, Ct));
        var separate = PluginPackageManagerAcceptanceTests.Need(await manager.InstallAsync(PluginPackageManagerAcceptanceTests.Decision(separateReview, ["email:read"], ["login-a"]), Ct));
        Assert.True((await manager.SetEnabledAsync(separate.Package.Identity, true, Ct)).IsOk);
        var separateGrant = PluginPackageManagerAcceptanceTests.Need(await manager.GetCurrentAsync("other-plugin", Ct))!;
        await using var separateBinding = PluginRuntimeBinding.Create(PackageTestRig.Profile, separateGrant, PluginFrameKind.Background);
        Assert.Null(PluginPackageManagerAcceptanceTests.Need(await storage.GetAsync(separateBinding, a, "note", Ct)));
        using var otherManager = rig.LeasedManager("agent-other");
        using var archive = PackageTestRig.Archive("export default 1", "email:read");
        var preview = PluginPackageManagerAcceptanceTests.Need(await otherManager.InspectAsync(archive, PluginPackageSource.LocalImport, Ct));
        var installed = PluginPackageManagerAcceptanceTests.Need(await otherManager.InstallAsync(PluginPackageManagerAcceptanceTests.Decision(preview, ["email:read"], ["login-a"]), Ct));
        Assert.True((await otherManager.SetEnabledAsync(installed.Package.Identity, true, Ct)).IsOk);
        var grant = PluginPackageManagerAcceptanceTests.Need(await otherManager.GetCurrentAsync(PackageTestRig.PluginId, Ct))!;
        await using var otherBinding = PluginRuntimeBinding.Create("agent-other", grant, PluginFrameKind.Background);
        Assert.Null(PluginPackageManagerAcceptanceTests.Need(await rig.Storage(otherManager, "agent-other").GetAsync(otherBinding, a, "note", Ct)));
        await binding.DisposeAsync();
    }

    [Fact]
    public async Task ConsentedUpdateRetainsStableDataButRejectsOldGeneration()
    {
        var rig = new PackageTestRig();
        var (manager, storage, oldBinding, capture) = await StartAsync(rig);
        Assert.True((await storage.SetAsync(oldBinding, capture, "note", PackageTestRig.StringValue("kept"), Ct)).IsOk);
        using var update = PackageTestRig.Archive("export default 2", "email:read");
        var preview = PluginPackageManagerAcceptanceTests.Need(await manager.InspectAsync(update, PluginPackageSource.LocalImport, Ct));
        var changed = PluginPackageManagerAcceptanceTests.Need(await manager.InstallAsync(PluginPackageManagerAcceptanceTests.Decision(preview, ["email:read"], ["login-a", "login-b"]), Ct));
        Assert.True((await manager.SetEnabledAsync(changed.Package.Identity, true, Ct)).IsOk);
        Assert.False((await storage.GetAsync(oldBinding, capture, "note", Ct)).IsOk);
        var grant = PluginPackageManagerAcceptanceTests.Need(await manager.GetCurrentAsync(PackageTestRig.PluginId, Ct))!;
        await using var next = PluginRuntimeBinding.Create(PackageTestRig.Profile, grant, PluginFrameKind.Background);
        Assert.Equal("kept", PluginPackageManagerAcceptanceTests.Need(await storage.GetAsync(next, capture, "note", Ct))!.StringValue, StringComparer.Ordinal);
        await oldBinding.DisposeAsync();
    }

    [Fact]
    public async Task ActiveLoginSwitchDuringStorageReadRefusesStaleCapturedResult()
    {
        var rig = new PackageTestRig();
        var (_, storage, binding, _) = await StartAsync(rig);
        var capture = PluginPackageManagerAcceptanceTests.Need(rig.Accounts.CaptureActive());
        rig.Data.LoadEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Data.ReleaseLoad = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = storage.GetAsync(binding, capture, "note", Ct);
        await rig.Data.LoadEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        rig.Accounts.Switch("login-b");
        rig.Data.ReleaseLoad.TrySetResult();
        Assert.False((await pending).IsOk);
        await binding.DisposeAsync();
    }

    [Fact]
    public async Task StorageUsesJsonStringifyRatherThanStructuredCloneEncoding()
    {
        var rig = new PackageTestRig();
        var (_, storage, binding, capture) = await StartAsync(rig);
        var builder = new PluginValueBuilder();
        var undef = builder.Undefined();
        var number = builder.Number(double.PositiveInfinity);
        var date = builder.Date(0);
        var array = builder.Array(undef, number);
        var value = builder.Build(builder.Object(("missing", undef), ("items", array), ("date", date)));
        Assert.True((await storage.SetAsync(binding, capture, "sample", value, Ct)).IsOk);
        var saved = PluginPackageManagerAcceptanceTests.Need(await storage.GetAsync(binding, capture, "sample", Ct))!;
        Assert.DoesNotContain("missing", saved.PropertyNames, StringComparer.Ordinal);
        Assert.Equal(PluginValueKind.Null, saved.Property("items").Item(0).Kind);
        Assert.Equal(PluginValueKind.Null, saved.Property("items").Item(1).Kind);
        Assert.Equal("1970-01-01T00:00:00.000Z", saved.Property("date").StringValue, StringComparer.Ordinal);
        await binding.DisposeAsync();
    }

    [Fact]
    public async Task ActiveLoginSwitchBeforeIssuanceCannotChangeStoredData()
    {
        var rig = new PackageTestRig();
        var (manager, storage, binding, owner) = await StartAsync(rig);
        using (manager)
        await using (binding)
        {
            var capture = PluginPackageManagerAcceptanceTests.Need(rig.Accounts.CaptureActive());
            rig.Data.LoadEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            rig.Data.ReleaseLoad = new(TaskCreationOptions.RunContinuationsAsynchronously);
            rig.Data.CommitEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = storage.SetAsync(binding, capture, "note", PackageTestRig.StringValue("stale"), Ct);
            await rig.Data.LoadEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
            rig.Accounts.Switch("login-b");
            rig.Accounts.Switch("login-a");
            rig.Data.ReleaseLoad.TrySetResult();
            Assert.False((await pending).IsOk);
            Assert.False(rig.Data.CommitEntered.Task.IsCompleted);
            Assert.Null(PluginPackageManagerAcceptanceTests.Need(await storage.GetAsync(binding, owner, "note", Ct)));
        }
    }

    [Fact]
    public async Task ACommitAlreadyIssuedStaysInTheOriginalLoginAndItsStaleResultIsRefused()
    {
        var rig = new PackageTestRig();
        var (manager, storage, binding, owner) = await StartAsync(rig);
        using (manager)
        await using (binding)
        {
            var capture = PluginPackageManagerAcceptanceTests.Need(rig.Accounts.CaptureActive());
            rig.Data.CommitEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            rig.Data.ReleaseCommit = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = storage.SetAsync(binding, capture, "note", PackageTestRig.StringValue("remember"), Ct);
            await rig.Data.CommitEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
            rig.Accounts.Switch("login-b");
            rig.Data.ReleaseCommit.TrySetResult();
            Assert.False((await pending).IsOk);
            var active = PluginPackageManagerAcceptanceTests.Need(rig.Accounts.CaptureActive());
            Assert.Null(PluginPackageManagerAcceptanceTests.Need(await storage.GetAsync(binding, active, "note", Ct)));
            Assert.Equal("remember", PluginPackageManagerAcceptanceTests.Need(await storage.GetAsync(binding, owner, "note", Ct))!.StringValue);
        }
    }

    [Fact]
    public async Task ConcurrentWritesKeepBothKeysInOneNamespace()
    {
        var rig = new PackageTestRig();
        var (manager, storage, binding, capture) = await StartAsync(rig);
        using (manager)
        await using (binding)
        {
            rig.Data.LoadEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            rig.Data.ReleaseLoad = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var first = storage.SetAsync(binding, capture, "first", PackageTestRig.StringValue("1"), Ct);
            await rig.Data.LoadEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
            var otherAdapter = rig.Storage(manager);
            var second = otherAdapter.SetAsync(binding, capture, "second", PackageTestRig.StringValue("2"), Ct);
            rig.Data.ReleaseLoad.TrySetResult();
            Assert.All(await Task.WhenAll(first, second), result => Assert.True(result.IsOk));
            Assert.Collection(PluginPackageManagerAcceptanceTests.Need(await storage.KeysAsync(binding, capture, Ct)),
                key => Assert.Equal("first", key), key => Assert.Equal("second", key));
        }
    }

    [Fact]
    public void MaximallySizedStorageResultFitsItsNativeResponseEnvelope()
    {
        var limits = PluginStorageSnapshot.ValueLimits;
        var value = new PluginValueBuilder();
        var keys = value.Array([.. Enumerable.Range(0, limits.MaxNodes - 1).Select(_ => value.String("sample"))]);
        var response = new PluginValueBuilder();
        var result = response.Import(value.Build(keys, limits));
        var wire = response.Build(response.Object(("type", response.String("api-response")), ("id", response.String("sample-response")),
            ("ok", response.Boolean(true)), ("result", result)));
        Assert.NotEmpty(PluginValueCodec.Encode(wire));
    }

    [Theory]
    [InlineData(1e-6, "0.000001")]
    [InlineData(1e-7, "1e-7")]
    [InlineData(1e20, "100000000000000000000")]
    [InlineData(1e21, "1e+21")]
    [InlineData(1e23, "1e+23")]
    [InlineData(double.Epsilon, "5e-324")]
    [InlineData(1000000000000000128d, "1000000000000000100")]
    public void JsonStringifyNumberUsesTheJavaScriptSpelling(double number, string text)
    {
        var builder = new PluginValueBuilder();
        Assert.Equal(text, PluginStorageJson.Stringify(builder.Build(builder.Number(number)), 100));
    }

    [Fact]
    public async Task StorageRejectsCyclesAndTopLevelUndefinedWithoutChangingPreviousValue()
    {
        var rig = new PackageTestRig();
        var (_, storage, binding, capture) = await StartAsync(rig);
        Assert.True((await storage.SetAsync(binding, capture, "sample", PackageTestRig.StringValue("original"), Ct)).IsOk);
        var builder = new PluginValueBuilder();
        var cycle = builder.Reserve();
        builder.SetArray(cycle, cycle);
        Assert.False((await storage.SetAsync(binding, capture, "sample", builder.Build(cycle), Ct)).IsOk);
        var undefBuilder = new PluginValueBuilder();
        Assert.False((await storage.SetAsync(binding, capture, "sample", undefBuilder.Build(undefBuilder.Undefined()), Ct)).IsOk);
        Assert.Equal("original", PluginPackageManagerAcceptanceTests.Need(await storage.GetAsync(binding, capture, "sample", Ct))!.StringValue, StringComparer.Ordinal);
        await binding.DisposeAsync();
    }

    [Fact]
    public async Task StorageEnforcesUtf16QuotaIncludingKeysBeforeMutation()
    {
        var rig = new PackageTestRig();
        var (_, storage, binding, capture) = await StartAsync(rig);
        Assert.True((await storage.SetAsync(binding, capture, "sample", PackageTestRig.StringValue("original"), Ct)).IsOk);
        Assert.False((await storage.SetAsync(binding, capture, new string('k', 257), PackageTestRig.StringValue("small"), Ct)).IsOk);
        var builder = new PluginValueBuilder();
        var large = builder.String(string.Concat(Enumerable.Repeat("😀", 524_288)));
        var limits = new PluginValueLimits(MaxWireBytes: 5_000_000, MaxStringBytes: 3_000_000);
        Assert.False((await storage.SetAsync(binding, capture, "sample", builder.Build(large, limits), Ct)).IsOk);
        Assert.Equal("original", PluginPackageManagerAcceptanceTests.Need(await storage.GetAsync(binding, capture, "sample", Ct))!.StringValue, StringComparer.Ordinal);
        await binding.DisposeAsync();
    }

    private static async Task<(PluginPackageManager Manager, PluginScopedStorage Storage, PluginRuntimeBinding Binding, PluginSessionCapture Capture)> StartAsync(PackageTestRig rig)
    {
        var manager = rig.LeasedManager();
        using var archive = PackageTestRig.Archive("export default 1", "email:read");
        var preview = PluginPackageManagerAcceptanceTests.Need(await manager.InspectAsync(archive, PluginPackageSource.LocalImport, Ct));
        var installed = PluginPackageManagerAcceptanceTests.Need(await manager.InstallAsync(PluginPackageManagerAcceptanceTests.Decision(preview, ["email:read"], ["login-a", "login-b"]), Ct));
        Assert.True((await manager.SetEnabledAsync(installed.Package.Identity, true, Ct)).IsOk);
        var grant = PluginPackageManagerAcceptanceTests.Need(await manager.GetCurrentAsync(PackageTestRig.PluginId, Ct))!;
        return (manager, rig.Storage(manager), PluginRuntimeBinding.Create(PackageTestRig.Profile, grant, PluginFrameKind.Background), PluginPackageManagerAcceptanceTests.Need(rig.Accounts.CaptureOwner("login-a")));
    }
}
