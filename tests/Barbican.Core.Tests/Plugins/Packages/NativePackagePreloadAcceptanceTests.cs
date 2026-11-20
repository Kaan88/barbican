// Barbican-native sidecar acceptance. Discovery and consent use inert metadata and never load the assembly.
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Barbican.Core.Plugins.Native;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;

namespace Barbican.Core.Tests.Plugins.Packages;

[Collection("Native package sentinel serial")]
public sealed class NativePackagePreloadAcceptanceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task InspectAndRejectedConsentNeverRunModuleInitializerConstructorOrManifest()
    {
        var directory = MarkerDirectory();
        Assert.False(Directory.Exists(directory));
        Directory.CreateDirectory(directory);
        try
        {
            var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "native-sentinel", "NativeSentinel.dll"), Ct);
            using var zip = NativeArchive(bytes);
            var manager = new PackageTestRig().Manager();
            var preview = PluginPackageManagerAcceptanceTests.Need(await manager.InspectAsync(zip, PluginPackageSource.LocalImport, Ct));
            Assert.Equal(PluginRuntimeKind.Native, preview.Candidate.RuntimeKind);
            AssertInert(directory);
            var denied = new PluginInstallDecision(preview.ReviewId, preview.Candidate.Identity, [], ["login-a"], approveNativeFullTrust: false);
            Assert.False((await manager.InstallAsync(denied, Ct)).IsOk);
            AssertInert(directory);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task TamperedDependencyAndAmbiguousDualManifestAreRejectedWithoutLoading()
    {
        var directory = MarkerDirectory();
        Assert.False(Directory.Exists(directory));
        Directory.CreateDirectory(directory);
        try
        {
            var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "native-sentinel", "NativeSentinel.dll"), Ct);
            using var tampered = NativeArchive(bytes, wrongHash: true);
            using var ambiguous = NativeArchive(bytes, includeJsManifest: true);
            var manager = new PackageTestRig().Manager();
            Assert.False((await manager.InspectAsync(tampered, PluginPackageSource.LocalImport, Ct)).IsOk);
            Assert.False((await manager.InspectAsync(ambiguous, PluginPackageSource.LocalImport, Ct)).IsOk);
            AssertInert(directory);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task ExplicitFullTrustPersistsForTheExactDisabledPackageWithoutLoadingIt()
    {
        var directory = MarkerDirectory();
        Assert.False(Directory.Exists(directory));
        Directory.CreateDirectory(directory);
        try
        {
            var bytes = await ReadSentinelAsync();
            var rig = new PackageTestRig();
            using var manager = rig.Manager();
            using var zip = NativeArchive(bytes);
            var preview = PluginPackageManagerAcceptanceTests.Need(await manager.InspectAsync(zip, PluginPackageSource.LocalImport, Ct));
            Assert.Equal(PluginExecutionTier.NativeFullTrust, preview.Candidate.RequestedTier);
            Assert.False((await manager.InstallAsync(new(preview.ReviewId, preview.Candidate.Identity, [], []), Ct)).IsOk);
            Assert.False((await manager.InstallAsync(new(preview.ReviewId, preview.Candidate.Identity, [], ["login-a"],
                approveNativeFullTrust: true), Ct)).IsOk);

            var decision = new PluginInstallDecision(preview.ReviewId, preview.Candidate.Identity, [], [], approveNativeFullTrust: true);
            var installed = PluginPackageManagerAcceptanceTests.Need(await manager.InstallAsync(decision, Ct));
            Assert.False(installed.Enabled);
            Assert.Equal(PluginExecutionTier.NativeFullTrust, installed.Grant!.Tier);
            using var reopened = rig.Manager();
            var current = Assert.Single(PluginPackageManagerAcceptanceTests.Need(await reopened.ListAsync(Ct)));
            Assert.Equal(installed.Package.Identity, current.Package.Identity);
            Assert.Equal(PluginExecutionTier.NativeFullTrust, current.Grant!.Tier);
            Assert.Null(PluginPackageManagerAcceptanceTests.Need(await reopened.GetCurrentAsync(current.Package.Identity.Id, Ct)));
            Assert.True((await reopened.SetEnabledAsync(current.Package.Identity, true, Ct)).IsOk);
            using var enabledManager = rig.Manager();
            Assert.True(Assert.Single(PluginPackageManagerAcceptanceTests.Need(await enabledManager.ListAsync(Ct))).Enabled);
            var enabled = PluginPackageManagerAcceptanceTests.Need(await enabledManager.GetCurrentAsync(current.Package.Identity.Id, Ct));
            Assert.NotNull(enabled);
            Assert.Equal(PluginExecutionTier.NativeFullTrust, enabled.Tier);
            AssertInert(directory);

            using var update = NativeArchive(bytes, change: sidecar => sidecar["description"] = "Updated synthetic package");
            var replacement = PluginPackageManagerAcceptanceTests.Need(await reopened.InspectAsync(update, PluginPackageSource.LocalImport, Ct));
            Assert.NotEqual(current.Package.Identity.Sha256, replacement.Candidate.Identity.Sha256, StringComparer.Ordinal);
            Assert.False((await reopened.InstallAsync(new(replacement.ReviewId, current.Package.Identity, [], [],
                approveNativeFullTrust: true), Ct)).IsOk);
            Assert.False((await reopened.InstallAsync(new(replacement.ReviewId, replacement.Candidate.Identity, [], []), Ct)).IsOk);
            Assert.Equal(current.Package.Identity, Assert.Single(PluginPackageManagerAcceptanceTests.Need(await reopened.ListAsync(Ct))).Package.Identity);
            AssertInert(directory);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task UndeclaredFilesInvalidIdentityEmptyDllsAndNonemptyDirectoriesAreRefused()
    {
        var bytes = await ReadSentinelAsync();
        using var manager = new PackageTestRig().Manager();
        using var extra = NativeArchive(bytes, extraFile: true);
        using var invalidId = NativeArchive(bytes, change: sidecar => sidecar["id"] = "bad-id\n");
        using var emptyLibrary = NativeArchive([]);
        using var directoryPayload = NativeArchive(bytes, directory: [1]);
        using var emptyDirectory = NativeArchive(bytes, directory: []);
        Assert.False((await manager.InspectAsync(extra, PluginPackageSource.LocalImport, Ct)).IsOk);
        Assert.False((await manager.InspectAsync(invalidId, PluginPackageSource.LocalImport, Ct)).IsOk);
        Assert.False((await manager.InspectAsync(emptyLibrary, PluginPackageSource.LocalImport, Ct)).IsOk);
        Assert.False((await manager.InspectAsync(directoryPayload, PluginPackageSource.LocalImport, Ct)).IsOk);
        Assert.True((await manager.InspectAsync(emptyDirectory, PluginPackageSource.LocalImport, Ct)).IsOk);
    }

    [Fact]
    public async Task RenewalCannotPairChangedArchiveBytesWithACachedNativeIdentity()
    {
        var rig = new PackageTestRig();
        using var manager = rig.LeasedManager();
        var bytes = await ReadSentinelAsync();
        using var zip = NativeArchive(bytes);
        var preview = PluginPackageManagerAcceptanceTests.Need(await manager.InspectAsync(zip, PluginPackageSource.LocalImport, Ct));
        var decision = new PluginInstallDecision(preview.ReviewId, preview.Candidate.Identity, [], [], approveNativeFullTrust: true);
        var installed = PluginPackageManagerAcceptanceTests.Need(await manager.InstallAsync(decision, Ct));
        Assert.Single(PluginPackageManagerAcceptanceTests.Need(await manager.ListAsync(Ct)));

        using var altered = NativeArchive(bytes, change: sidecar => sidecar["description"] = "Changed synthetic archive");
        Assert.True((await rig.Persistence.PutContentAsync(PackageTestRig.Profile, installed.Package.Identity, altered.ToArray(), Ct)).IsOk);
        Assert.False((await manager.ReviewAsync(installed.Package.Identity, Ct)).IsOk);
        Assert.False((await manager.SetEnabledAsync(installed.Package.Identity, true, Ct)).IsOk);
    }

    [Fact]
    public async Task EnabledApprovedArchiveProvidesReaderDetailsUntilDisabled()
    {
        if (!OperatingSystem.IsWindows()) return;
        var rig = new PackageTestRig();
        var revocation = new PluginRevocationCoordinator(PackageTestRig.Profile);
        using var manager = rig.LeasedManager(revocation: revocation);
        using var plugins = new NativePlugins(() => new NativePluginHostOptions { DataRoot = Path.GetTempPath() });
        var factory = plugins.CreatePackageFactory(manager, revocation);
        const string library = "Barbican.Plugin.SpamScore.dll";
        var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "plugins", "spam-score", library), Ct);
        using var zip = NativeArchive(bytes, change: sidecar =>
        {
            sidecar["id"] = "spam-score";
            sidecar["version"] = "0.1.0";
        }, library: library);
        var preview = PluginPackageManagerAcceptanceTests.Need(await manager.InspectAsync(zip, PluginPackageSource.LocalImport, Ct));
        var installed = PluginPackageManagerAcceptanceTests.Need(await manager.InstallAsync(
            new(preview.ReviewId, preview.Candidate.Identity, [], [], approveNativeFullTrust: true), Ct));
        var email = JsonNode.Parse("""{"headers":[{"name":"X-Spam-Status","value":"Yes, score=7.5 required=5.0"}]}""")!;
        using var empty = JsonDocument.Parse("{}");
        var state = new PluginHostState("en", empty.RootElement, empty.RootElement, empty.RootElement, empty.RootElement);
        var props = PluginValue.FromJson(empty.RootElement);
        Assert.False((await factory.StartAsync(Request(installed), Ct)).IsOk);
        Assert.Empty(await plugins.EmailDetailsAsync(email, Ct));

        Assert.True((await manager.SetEnabledAsync(installed.Package.Identity, true, Ct)).IsOk);
        var enabled = PluginPackageManagerAcceptanceTests.Need(await manager.FindAsync(preview.Candidate.Identity.Id, Ct))!;
        using var changed = NativeArchive(bytes, change: sidecar =>
        {
            sidecar["id"] = "spam-score";
            sidecar["version"] = "0.1.0";
            sidecar["description"] = "Substitute content";
        }, library: library);
        Assert.True((await rig.Persistence.PutContentAsync(PackageTestRig.Profile, enabled.Package.Identity, changed.ToArray(), Ct)).IsOk);
        Assert.False((await factory.StartAsync(Request(enabled), Ct)).IsOk);
        Assert.True((await rig.Persistence.PutContentAsync(PackageTestRig.Profile, enabled.Package.Identity, zip.ToArray(), Ct)).IsOk);
        await using var runtime = PluginPackageManagerAcceptanceTests.Need(await factory.StartAsync(Request(enabled), Ct));
        Assert.False((await factory.StartAsync(Request(enabled), Ct)).IsOk);
        Assert.Equal("Spam analysis", Assert.Single(await plugins.EmailDetailsAsync(email, Ct)).Label);
        Assert.True((await manager.SetEnabledAsync(enabled.Package.Identity, false, Ct)).IsOk);
        Assert.False(runtime.Binding.IsAlive);
        Assert.Empty(await plugins.EmailDetailsAsync(email, Ct));
        Assert.False((await factory.StartAsync(Request(enabled), Ct)).IsOk);

        PluginRuntimeStart Request(PluginInstallation item) =>
            new(item, item.Grant!, PackageTestRig.Profile, PluginFrameKind.Background, null, props, state);
    }

    private static void AssertInert(string directory)
    {
        Assert.False(File.Exists(Path.Combine(directory, "activity")));
        Assert.DoesNotContain(AppDomain.CurrentDomain.GetAssemblies(),
            assembly => string.Equals(assembly.GetName().Name, "NativeSentinel", StringComparison.Ordinal));
    }

    private static Task<byte[]> ReadSentinelAsync() =>
        File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "native-sentinel", "NativeSentinel.dll"), Ct);

    private static string MarkerDirectory() => Path.Combine(Path.GetTempPath(), "barbican-native-sentinel-" + Environment.ProcessId.ToString(CultureInfo.InvariantCulture));

    private static MemoryStream NativeArchive(byte[] assembly, bool wrongHash = false, bool includeJsManifest = false,
        bool extraFile = false, Action<JsonObject>? change = null, byte[]? directory = null, string library = "NativeSentinel.dll")
    {
        var digest = wrongHash ? new string('0', 64) : Convert.ToHexStringLower(SHA256.HashData(assembly));
        var sidecar = new JsonObject
        {
            ["schemaVersion"] = 1, ["api"] = 1, ["id"] = "sentinel-fixture", ["name"] = "Sentinel", ["version"] = "1.0.0",
            ["description"] = "Synthetic", ["mainAssembly"] = library, ["hooks"] = new JsonArray("email.details"),
            ["dependencies"] = new JsonObject { [library] = digest },
        };
        change?.Invoke(sidecar);
        var entries = new List<(string Path, byte[] Bytes)>
        {
            ("native-plugin.json", Encoding.UTF8.GetBytes(sidecar.ToJsonString())),
            (library, assembly),
        };
        if (includeJsManifest) entries.Add(("manifest.json", Encoding.UTF8.GetBytes("{}")));
        if (extraFile) entries.Add(("undeclared.json", Encoding.UTF8.GetBytes("{}")));
        if (directory is not null) entries.Add(("spare/", directory));
        return PackageTestRig.Zip([.. entries]);
    }
}
