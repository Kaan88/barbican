// M6b: inert ZIP inspection, Bulwark integration/fixtures/plugin-api-probe and lib/extension-package.ts.
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Barbican.Core.Plugins.Packages.Inspection;

namespace Barbican.Core.Tests.Plugins.Inspection;

public sealed class PluginPackageInspectorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly string Fixture = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../Plugins/Inspection/Fixtures"));
    private const string Basic = "{\"id\":\"test-plugin\",\"name\":\"Test\",\"version\":\"1.0.0\",\"author\":\"Example\",\"type\":\"hook\"}";

    [Fact]
    public async Task UnchangedApiProbeRunsAgainstItsBulwarkCompatibilityVersion()
    {
        var manifest = await File.ReadAllTextAsync(Path.Combine(Fixture, "api-probe-manifest.json"), Ct);
        var code = await File.ReadAllTextAsync(Path.Combine(Fixture, "api-probe.js"), Ct);
        using var zip = Archive(("manifest.json", manifest), ("index.js", code));
        var inspected = await PluginPackageInspector.InspectAsync(zip, "1.21.2", Ct);
        Assert.True(inspected.IsOk, inspected.Error?.Message);
        Assert.Equal("it-plugin-api-probe", inspected.Value!.Id, StringComparer.Ordinal);
        Assert.Equal(code, Encoding.UTF8.GetString(inspected.Value.Resources["index.js"].AsSpan()), StringComparer.Ordinal);
        Assert.Equal(64, inspected.Value.WholeArchiveSha256.Length);
        Assert.Equal(64, inspected.Value.BundleHash.Length);
        Assert.Equal(64, inspected.Value.ApprovalHash.Length);
    }

    [Theory]
    [InlineData("1.14.9", false)]
    [InlineData("1.15.0", true)]
    [InlineData("1.15.0-alpha", true)]
    [InlineData("invalid", false)]
    public async Task MinimumUsesExplicitCompatibleBulwarkVersion(string host, bool allowed)
    {
        var manifest = Basic[..^1] + ",\"minAppVersion\":\"1.15.0\"}";
        using var zip = Archive(("manifest.json", manifest), ("index.js", "export default {}"));
        Assert.Equal(allowed, (await PluginPackageInspector.InspectAsync(zip, host, Ct)).IsOk);
    }

    [Fact]
    public async Task VersionComparisonMatchesJavaScriptNumericPrecision()
    {
        var manifest = Basic[..^1] + ",\"minAppVersion\":\"9007199254740993.0.0\"}";
        using var zip = Archive(("manifest.json", manifest), ("index.js", "source"));
        Assert.True((await PluginPackageInspector.InspectAsync(zip, "9007199254740992.0.0", Ct)).IsOk);
    }

    [Fact]
    public async Task NestedRootIsNormalizedAndValuesAreDetachedFromCaller()
    {
        using var zip = Archive(("folder/manifest.json", Basic), ("folder/index.js", "source"), ("folder/image.svg", "<svg/>"));
        var result = await PluginPackageInspector.InspectAsync(zip, "1.21.2", Ct);
        Assert.True(result.IsOk, result.Error?.Message);
        zip.SetLength(0);
        Assert.Equal("source", Encoding.UTF8.GetString(result.Value!.Resources["index.js"].AsSpan()), StringComparer.Ordinal);
        Assert.True(result.Value.Resources.ContainsKey("image.svg"));
    }

    [Theory]
    [InlineData("../escape.js")]
    [InlineData("folder/../escape.js")]
    [InlineData("C:/escape.js")]
    [InlineData("folder/evil.js:stream")]
    [InlineData("folder/evil.js ")]
    [InlineData("folder/COM1.js")]
    [InlineData("folder/COM¹.js")]
    [InlineData("folder/evil?.js")]
    [InlineData("folder\\\\evil.js")]
    public async Task UnsafePathsRefused(string path)
    {
        using var zip = Archive(("manifest.json", Basic), ("index.js", "source"), (path, "x"));
        Assert.False((await PluginPackageInspector.InspectAsync(zip, "1.21.2", Ct)).IsOk);
    }

    [Fact]
    public async Task DuplicateCaseAliasesAndSymlinksRefused()
    {
        using var duplicate = Archive(("manifest.json", Basic), ("index.js", "a"), ("INDEX.js", "b"));
        Assert.False((await PluginPackageInspector.InspectAsync(duplicate, "1.21.2", Ct)).IsOk);
        using var link = ArchiveWithLink();
        Assert.False((await PluginPackageInspector.InspectAsync(link, "1.21.2", Ct)).IsOk);
    }

    [Fact]
    public async Task StandardStreamingZipDataDescriptorsAreAccepted()
    {
        using var zip = ArchiveStreaming();
        var result = await PluginPackageInspector.InspectAsync(zip, "1.21.2", Ct);
        Assert.True(result.IsOk, result.Error?.Message);
    }

    [Fact]
    public async Task MismatchedLocalAndCentralNamesAreRejected()
    {
        using var source = Archive(("manifest.json", Basic), ("index.js", "source"));
        var bytes = source.ToArray();
        // Local filename starts immediately after the fixed 30-byte header.
        bytes[30] = (byte)'x';
        using var altered = new MemoryStream(bytes);
        Assert.False((await PluginPackageInspector.InspectAsync(altered, "1.21.2", Ct)).IsOk);
    }

    [Fact]
    public async Task CorruptStoredEntryIsRejectedEvenWhenHeadersAgree()
    {
        using var source = ArchiveUncompressed();
        var bytes = source.ToArray();
        var offset = bytes.AsSpan().IndexOf(Encoding.ASCII.GetBytes("unique-source"));
        Assert.True(offset > 0);
        bytes[offset] ^= 1;
        using var altered = new MemoryStream(bytes);
        Assert.False((await PluginPackageInspector.InspectAsync(altered, "1.21.2", Ct)).IsOk);
    }

    [Fact]
    public async Task ReparseAttributesAreRejected()
    {
        using var bytes = ArchiveWithReparse();
        Assert.False((await PluginPackageInspector.InspectAsync(bytes, "1.21.2", Ct)).IsOk);
    }

    [Fact]
    public async Task TotalInflatedSizeAndCompressedInputAreBounded()
    {
        using var archive = ArchiveOverTotalLimit();
        Assert.False((await PluginPackageInspector.InspectAsync(archive, "1.21.2", Ct)).IsOk);
        using var oversized = new MemoryStream(new byte[(5 * 1024 * 1024) + 1]);
        Assert.False((await PluginPackageInspector.InspectAsync(oversized, "1.21.2", Ct)).IsOk);
    }

    [Fact]
    public async Task OversizedInflatedEntryAndTooManyEntriesAreRefused()
    {
        using var oversized = Archive(("manifest.json", Basic), ("index.js", new string('x', (20 * 1024 * 1024) + 1)));
        Assert.False((await PluginPackageInspector.InspectAsync(oversized, "1.21.2", Ct)).IsOk);
        using var many = Archive([("manifest.json", Basic), ("index.js", "x"), .. Enumerable.Range(0, 199).Select(i => ($"x{i}.js", ""))]);
        Assert.False((await PluginPackageInspector.InspectAsync(many, "1.21.2", Ct)).IsOk);
    }

    [Fact]
    public async Task UnknownRequiredPermissionRefusesButUnknownOptionalAndDeprecatedAreOmitted()
    {
        var optional = Basic[..^1] + ",\"permissions\":[\"email:read\",\"smime:read\"],\"optionalPermissions\":[\"not:available\",\"email:write\"]}";
        using var zip = Archive(("manifest.json", optional), ("index.js", "source"));
        var result = await PluginPackageInspector.InspectAsync(zip, "1.21.2", Ct);
        Assert.True(result.IsOk, result.Error?.Message);
        Assert.Contains("email:read", result.Value!.Permissions, StringComparer.Ordinal);
        Assert.Contains("email:write", result.Value.Permissions, StringComparer.Ordinal);
        Assert.DoesNotContain("smime:read", result.Value.Permissions, StringComparer.Ordinal);
        Assert.DoesNotContain("not:available", result.Value.Permissions, StringComparer.Ordinal);
        Assert.NotEmpty(result.Value.Warnings);
        using var invalid = Archive(("manifest.json", Basic[..^1] + ",\"permissions\":[\"not:available\"]}"), ("index.js", "x"));
        Assert.False((await PluginPackageInspector.InspectAsync(invalid, "1.21.2", Ct)).IsOk);
    }

    [Fact]
    public async Task ApprovalHashMatchesNodeCanonicalUnicodeAndNumericVector()
    {
        const string manifest = """{"id":"test-plugin","name":"Test","version":"1.0.0","author":"Example","description":"\ud800 café 😀","type":"hook","configSchema":{"region":{"type":"string","label":"µ","default":"\udfff","numeric":1e21,"tiny":1e-7,"threshold":1e-6}},"locales":{"en":{"\ud800":"Hi","emoji":"😀"}}}""";
        using var zip = Archive(("manifest.json", manifest), ("index.js", "console.log(\"hello\");"));
        var result = await PluginPackageInspector.InspectAsync(zip, "1.21.2", Ct);
        Assert.True(result.IsOk, result.Error?.Message);
        Assert.Equal("1aec61254534a87285e5c926bb84d84cbb568843b4cc9ab504e5abb8bf8b12d9", result.Value!.ApprovalHash, StringComparer.Ordinal);
        Assert.Contains("\\ud800", result.Value.ManifestJson, StringComparison.Ordinal);
        Assert.Contains("\\udfff", result.Value.ManifestJson, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1e-7", "3426a997b8abfa081a9c1e3f3411b979aca6681bb6d2d5e39711d966b3956562")]
    [InlineData("1e-6", "466a24234dadea1e72cbf44a9769b607b4d5282f1b4c838bf861ead03312753b")]
    [InlineData("1e21", "9c0ed8b2ddaef2034c414d43118a60782d144e2a567b40c7d371944977bf865e")]
    [InlineData("1e20", "79afb5e2aceaf0399266a804f402a74ebffbf70ad744f59c140aef8fbfac9f88")]
    [InlineData("333333333.33333329", "eff525d0e30f091935088af1deac5d5cf57cc8919812cf4e346359df236f741c")]
    [InlineData("1000000000000000100", "53c283d4f952264eb5eea89271a9dc4e0a570f491788b21a72f48eb5ce89910f")]
    [InlineData("9007199254740993", "1c5cfd0ee7be50461d514e73321a35c33ad751592c38d8bf54c37f841417d05e")]
    [InlineData("-0", "70e4ecf6761a24695e609b295ff56207ed8ded2e0fd50f837c2aad0115756995")]
    [InlineData("1.2345678901234568", "de8ce51f5020afeaf7374d97e914a23f9cfa75f3423db7a9fb3a0cbaa2e4f7d3")]
    public async Task ApprovalHashMatchesNodeNumberFormatting(string number, string expected)
    {
        var manifest = Basic[..^1] + ",\"configSchema\":{\"a\":{\"type\":\"number\",\"label\":\"Number\",\"default\":" + number + "}}}";
        using var zip = Archive(("manifest.json", manifest), ("index.js", "source"));
        var result = await PluginPackageInspector.InspectAsync(zip, "1.21.2", Ct);
        Assert.True(result.IsOk, result.Error?.Message);
        Assert.Equal(expected, result.Value!.ApprovalHash, StringComparer.Ordinal);
    }

    [Fact]
    public async Task DirectoryAliasesAndFileDirectoryConflictsAreRefused()
    {
        using var alias = Archive(("manifest.json", Basic), ("index.js", "x"), ("dir/a.js", "x"), ("DIR/b.js", "x"));
        Assert.False((await PluginPackageInspector.InspectAsync(alias, "1.21.2", Ct)).IsOk);
        using var collision = Archive(("manifest.json", Basic), ("index.js", "x"), ("dir", "x"), ("dir/a.js", "x"));
        Assert.False((await PluginPackageInspector.InspectAsync(collision, "1.21.2", Ct)).IsOk);
    }

    [Fact]
    public async Task Utf8BomIsDecodedLikeBulwarkTextDecoder()
    {
        using var zip = Archive(("manifest.json", "\ufeff" + Basic), ("index.js", "\ufeffsource"));
        using var plain = Archive(("manifest.json", Basic), ("index.js", "source"));
        var withBom = await PluginPackageInspector.InspectAsync(zip, "1.21.2", Ct);
        var withoutBom = await PluginPackageInspector.InspectAsync(plain, "1.21.2", Ct);
        Assert.True(withBom.IsOk, withBom.Error?.Message);
        Assert.True(withoutBom.IsOk, withoutBom.Error?.Message);
        Assert.Equal(withoutBom.Value!.ApprovalHash, withBom.Value!.ApprovalHash, StringComparer.Ordinal);
        Assert.Equal(withoutBom.Value.BundleHash, withBom.Value.BundleHash, StringComparer.Ordinal);
        Assert.NotEqual(withoutBom.Value.WholeArchiveSha256, withBom.Value.WholeArchiveSha256, StringComparer.Ordinal);
    }

    [Fact]
    public async Task ApprovalHashesMatchDeterministicNodeDoubleVectors()
    {
        using var vectors = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(Fixture, "node-number-hashes.json"), Ct));
        foreach (var pair in vectors.RootElement.EnumerateArray())
        {
            var number = pair[0].GetString();
            var expected = pair[1].GetString();
            var manifest = Basic[..^1] + ",\"configSchema\":{\"a\":{\"type\":\"number\",\"label\":\"Number\",\"default\":" + number + "}}}";
            using var zip = Archive(("manifest.json", manifest), ("index.js", "source"));
            var result = await PluginPackageInspector.InspectAsync(zip, "1.21.2", Ct);
            Assert.True(result.IsOk, result.Error?.Message);
            Assert.Equal(expected, result.Value!.ApprovalHash, StringComparer.Ordinal);
        }
    }

    [Fact]
    public async Task HttpCredentialOriginRequiresExactInjectedAdminAllowlist()
    {
        const string extra = ""","httpOrigins":["http://intranet.example:8080"],"credentials":{"local":{"label":"Local","scope":"user","inject":{"type":"bearer"},"origin":"http://intranet.example:8080/path"}}}""";
        var manifest = Basic[..^1] + extra;
        using var untrusted = Archive(("manifest.json", manifest), ("index.js", "source"));
        using var wrongPort = Archive(("manifest.json", manifest), ("index.js", "source"));
        using var allowed = Archive(("manifest.json", manifest), ("index.js", "source"));
        Assert.False((await PluginPackageInspector.InspectAsync(untrusted, "1.21.2", Ct)).IsOk);
        Assert.False((await PluginPackageInspector.InspectAsync(wrongPort, "1.21.2", ["http://intranet.example:8081"], Ct)).IsOk);
        var result = await PluginPackageInspector.InspectAsync(allowed, "1.21.2", ["http://intranet.example:8080"], Ct);
        Assert.True(result.IsOk, result.Error?.Message);
        using var parsed = JsonDocument.Parse(result.Value!.ManifestJson);
        Assert.Equal("http://intranet.example:8080", parsed.RootElement.GetProperty("credentials").GetProperty("local").GetProperty("origin").GetString(), StringComparer.Ordinal);
        Assert.False(parsed.RootElement.TryGetProperty("httpOrigins", out _));
    }

    [Fact]
    public async Task PreservesValidSchemasOriginsAndCredentials()
    {
        const string extra = ""","settingsSchema":{"mode":{"type":"select","label":"Mode","default":"a","options":["a","b"]}},"configSchema":{"secret":{"type":"secret","label":"Secret"}},"credentials":{"cloud":{"label":"Cloud","scope":"admin","inject":{"type":"bearer"},"secretKey":"secret","origin":"https://example.org"}},"httpOrigins":["https://example.org"],"apiPostPaths":["/api/example/"],"locales":{"en":{"hello":"Hello"}}}""";
        using var zip = Archive(("manifest.json", Basic[..^1] + extra), ("index.js", "eval('test')"));
        var result = await PluginPackageInspector.InspectAsync(zip, "1.21.2", Ct);
        Assert.True(result.IsOk, result.Error?.Message);
        using var manifest = JsonDocument.Parse(result.Value!.ManifestJson);
        Assert.Equal("secret", manifest.RootElement.GetProperty("credentials").GetProperty("cloud").GetProperty("secretKey").GetString(), StringComparer.Ordinal);
        Assert.Equal("a", manifest.RootElement.GetProperty("settingsSchema").GetProperty("mode").GetProperty("default").GetString(), StringComparer.Ordinal);
        Assert.True(manifest.RootElement.TryGetProperty("locales", out _));
        Assert.True(manifest.RootElement.TryGetProperty("httpOrigins", out _));
        Assert.True(manifest.RootElement.TryGetProperty("apiPostPaths", out _));
        Assert.NotEmpty(result.Value.Warnings);
    }

    private static MemoryStream Archive(params (string Path, string Text)[] entries)
    {
        var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, text) in entries)
            {
                using var writer = new StreamWriter(zip.CreateEntry(path, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
                writer.Write(text);
            }
        }
        bytes.Position = 0;
        return bytes;
    }

    private static MemoryStream ArchiveStreaming()
    {
        using var buffer = new NonSeekableArchiveStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open())) writer.Write(Basic);
            using var code = new StreamWriter(zip.CreateEntry("index.js").Open());
            code.Write("source");
        }
        return new MemoryStream(buffer.Bytes());
    }

    private static MemoryStream ArchiveOverTotalLimit()
    {
        var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var manifest = new StreamWriter(zip.CreateEntry("manifest.json").Open())) manifest.Write(Basic);
            using (var entry = new StreamWriter(zip.CreateEntry("index.js").Open())) entry.Write("source");
            var block = new byte[8192];
            for (var i = 0; i < 3; i++)
            {
                using var stream = zip.CreateEntry($"part{i}.js", CompressionLevel.Optimal).Open();
                for (var j = 0; j < 1792; j++) stream.Write(block);
            }
        }
        bytes.Position = 0;
        return bytes;
    }

    private static MemoryStream ArchiveUncompressed()
    {
        var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("manifest.json", CompressionLevel.NoCompression).Open())) writer.Write(Basic);
            using var source = new StreamWriter(zip.CreateEntry("index.js", CompressionLevel.NoCompression).Open());
            source.Write("unique-source");
        }
        bytes.Position = 0;
        return bytes;
    }

    private static MemoryStream ArchiveWithReparse()
    {
        var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open())) writer.Write(Basic);
            var entry = zip.CreateEntry("index.js");
            entry.ExternalAttributes = 0x400;
            using var writer2 = new StreamWriter(entry.Open());
            writer2.Write("source");
        }
        bytes.Position = 0;
        return bytes;
    }

    private static MemoryStream ArchiveWithLink()
    {
        var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open())) writer.Write(Basic);
            using (var writer = new StreamWriter(zip.CreateEntry("index.js").Open())) writer.Write("x");
            var link = zip.CreateEntry("link.js");
            link.ExternalAttributes = unchecked((int)0xA1FF0000);
        }
        bytes.Position = 0;
        return bytes;
    }
}
