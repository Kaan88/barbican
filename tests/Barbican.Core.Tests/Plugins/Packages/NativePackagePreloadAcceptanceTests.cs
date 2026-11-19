// RED: Barbican-native sidecar is a host format, not a Bulwark JS manifest; no plugin assembly may load during discovery.
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Barbican.Core.Plugins.Packages;

namespace Barbican.Core.Tests.Plugins.Packages;

[Collection("Native package sentinel serial")]
public sealed class NativePackagePreloadAcceptanceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Red("m6b-packages")]
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
            Assert.False(File.Exists(Path.Combine(directory, "activity")));
            var denied = new PluginInstallDecision(preview.ReviewId, preview.Candidate.Identity, [], ["login-a"], approveNativeFullTrust: false);
            Assert.False((await manager.InstallAsync(denied, Ct)).IsOk);
            Assert.False(File.Exists(Path.Combine(directory, "activity")));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    [Red("m6b-packages")]
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
            Assert.False(File.Exists(Path.Combine(directory, "activity")));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static string MarkerDirectory() => Path.Combine(Path.GetTempPath(), "barbican-native-sentinel-" + Environment.ProcessId.ToString(CultureInfo.InvariantCulture));

    private static MemoryStream NativeArchive(byte[] assembly, bool wrongHash = false, bool includeJsManifest = false)
    {
        var digest = wrongHash ? new string('0', 64) : Convert.ToHexStringLower(SHA256.HashData(assembly));
        var sidecar = new JsonObject
        {
            ["schemaVersion"] = 1, ["api"] = 1, ["id"] = "sentinel-fixture", ["name"] = "Sentinel", ["version"] = "1.0.0",
            ["description"] = "Synthetic", ["mainAssembly"] = "NativeSentinel.dll", ["hooks"] = new JsonArray("email.details"),
            ["dependencies"] = new JsonObject { ["NativeSentinel.dll"] = digest },
        };
        var entries = new List<(string Path, byte[] Bytes)>
        {
            ("native-plugin.json", Encoding.UTF8.GetBytes(sidecar.ToJsonString())),
            ("NativeSentinel.dll", assembly),
        };
        if (includeJsManifest) entries.Add(("manifest.json", Encoding.UTF8.GetBytes("{}")));
        return PackageTestRig.Zip([.. entries]);
    }
}
