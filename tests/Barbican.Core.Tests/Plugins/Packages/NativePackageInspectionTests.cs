// Barbican host-native sidecar v1: adversarial ZIP and consent inspection without loading assemblies.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Barbican.Core.Plugins.Packages.Inspection;
namespace Barbican.Core.Tests.Plugins.Packages;

public sealed class NativePackageInspectionTests
{
    [Theory]
    [InlineData("../escape.dll")]
    [InlineData("folder/../../escape.dll")]
    [InlineData("main.dll:stream")]
    [InlineData("CON.dll")]
    [InlineData("folder\\main.dll")]
    public async Task UnsafeMemberNamesAreRejected(string path)
    {
        using var archive = Archive(path, path);
        Assert.False((await NativePackageInspector.InspectAsync(archive, TestContext.Current.CancellationToken)).IsOk);
    }

    [Fact]
    public async Task UndeclaredMembersAndCaseAliasedPathsAreRejected()
    {
        using var extra = Archive("main.dll", "main.dll", extra: "extra.dll");
        using var alias = Archive("main.dll", "main.dll", extra: "MAIN.dll");
        Assert.False((await NativePackageInspector.InspectAsync(extra, TestContext.Current.CancellationToken)).IsOk);
        Assert.False((await NativePackageInspector.InspectAsync(alias, TestContext.Current.CancellationToken)).IsOk);
    }

    [Fact]
    public async Task MissingMainAndWrongDependencyDigestAreRejected()
    {
        using var missing = Archive("main.dll", "missing.dll");
        using var wrong = Archive("main.dll", "main.dll", wrongHash: true);
        Assert.False((await NativePackageInspector.InspectAsync(missing, TestContext.Current.CancellationToken)).IsOk);
        Assert.False((await NativePackageInspector.InspectAsync(wrong, TestContext.Current.CancellationToken)).IsOk);
    }

    [Fact]
    public async Task SingleDirectoryPackageKeepsExactArchiveIdentityAndValidatedBytes()
    {
        using var archive = Archive("nested/main.dll", "main.dll", nested: true);
        var bytes = archive.ToArray();
        var inspected = PluginPackageManagerAcceptanceTests.Need(await NativePackageInspector.InspectAsync(archive, TestContext.Current.CancellationToken));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), inspected.WholeArchiveSha256, StringComparer.Ordinal);
        Assert.Equal(new byte[] { 1, 2, 3 }, inspected.Resources["main.dll"].ToArray());
    }

    private static MemoryStream Archive(string member, string entry, string? extra = null, bool wrongHash = false, bool nested = false)
    {
        byte[] body = [1, 2, 3];
        var hash = wrongHash ? new string('0', 64) : Convert.ToHexStringLower(SHA256.HashData(body));
        var manifest = new JsonObject
        {
            ["schemaVersion"] = 1, ["api"] = 1, ["id"] = "fixture-native", ["name"] = "Fixture", ["version"] = "1.0.0",
            ["description"] = "Synthetic", ["mainAssembly"] = entry, ["hooks"] = new JsonArray(),
            ["dependencies"] = new JsonObject { [entry] = hash },
        };
        var entries = new List<(string Path, byte[] Bytes)> { ((nested ? "nested/" : "") + "native-plugin.json", Encoding.UTF8.GetBytes(manifest.ToJsonString())), (member, body) };
        if (extra is not null) entries.Add((extra, body));
        return PackageTestRig.Zip([.. entries]);
    }
}
