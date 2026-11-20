using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using BootstrapAssets = Barbican.App.Plugins.Sandbox.PluginSandboxBootstrapAssets;

namespace Barbican.Automation.Tests.Plugins;

public sealed class PluginSandboxBootstrapAssetsTests
{
    [Fact]
    public async Task Returns_immutable_bytes_only_after_every_file_matches()
    {
        var ct = TestContext.Current.CancellationToken;
        using var folder = new TestFolder();
        var pins = folder.Write("one.js", "one", "entry", "manifest.json", "manifest", "manifest");
        var assets = await BootstrapAssets.VerifyAsync(folder.Path, pins, ct);
        Assert.Equal("entry", assets["one.js"].Purpose);
        Assert.Equal("one", Encoding.UTF8.GetString(assets["one.js"].Content.AsSpan()));
        Assert.Equal(2, assets.Count);
        await File.WriteAllTextAsync(System.IO.Path.Combine(folder.Path, "manifest.json"), "changed!", ct);
        await Assert.ThrowsAsync<InvalidDataException>(() => BootstrapAssets.VerifyAsync(folder.Path, pins, ct));
        Assert.Equal("one", Encoding.UTF8.GetString(assets["one.js"].Content.AsSpan()));
    }

    [Fact]
    public async Task Rejects_extra_missing_and_changed_files()
    {
        var ct = TestContext.Current.CancellationToken;
        using var folder = new TestFolder();
        var pins = folder.Write("one.js", "one", "entry");
        await File.WriteAllTextAsync(System.IO.Path.Combine(folder.Path, "extra"), "x", ct);
        await Assert.ThrowsAsync<InvalidDataException>(() => BootstrapAssets.VerifyAsync(folder.Path, pins, ct));
        File.Delete(System.IO.Path.Combine(folder.Path, "extra"));
        await File.WriteAllTextAsync(System.IO.Path.Combine(folder.Path, "one.js"), "two", ct);
        await Assert.ThrowsAsync<InvalidDataException>(() => BootstrapAssets.VerifyAsync(folder.Path, pins, ct));
        File.Delete(System.IO.Path.Combine(folder.Path, "one.js"));
        await Assert.ThrowsAsync<InvalidDataException>(() => BootstrapAssets.VerifyAsync(folder.Path, pins, ct));
    }

    [Fact]
    public async Task Rejects_invalid_names_and_duplicate_pins()
    {
        var ct = TestContext.Current.CancellationToken;
        using var folder = new TestFolder();
        var pins = folder.Write("one.js", "one", "entry");
        await Assert.ThrowsAsync<InvalidDataException>(() => BootstrapAssets.VerifyAsync(folder.Path, [pins[0], pins[0]], ct));
        await Assert.ThrowsAsync<InvalidDataException>(() => BootstrapAssets.VerifyAsync(folder.Path, [pins[0] with { Name = "../one.js" }], ct));
    }

    [Fact]
    public async Task Rejects_short_and_long_contents_and_honors_cancellation()
    {
        var ct = TestContext.Current.CancellationToken;
        using var folder = new TestFolder();
        var pins = folder.Write("one.js", "one", "entry");
        var path = System.IO.Path.Combine(folder.Path, "one.js");
        await File.WriteAllTextAsync(path, "on", ct);
        await Assert.ThrowsAsync<EndOfStreamException>(() => BootstrapAssets.VerifyAsync(folder.Path, pins, ct));
        await File.WriteAllTextAsync(path, "ones", ct);
        await Assert.ThrowsAsync<InvalidDataException>(() => BootstrapAssets.VerifyAsync(folder.Path, pins, ct));
        using var canceled = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await canceled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => BootstrapAssets.VerifyAsync(folder.Path, pins, canceled.Token));
    }

    [Fact]
    public async Task Rejects_a_directory_as_an_inventory_entry()
    {
        var ct = TestContext.Current.CancellationToken;
        using var folder = new TestFolder();
        var pins = folder.Write("one.js", "one", "entry");
        File.Delete(System.IO.Path.Combine(folder.Path, "one.js"));
        Directory.CreateDirectory(System.IO.Path.Combine(folder.Path, "one.js"));
        await Assert.ThrowsAsync<InvalidDataException>(() => BootstrapAssets.VerifyAsync(folder.Path, pins, ct));
    }

    private sealed class TestFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "barbican-bootstrap-" + Guid.NewGuid().ToString("N"));

        public TestFolder() => Directory.CreateDirectory(Path);

        public ImmutableArray<BootstrapAssets.AssetPin> Write(params string[] values)
        {
            var pins = ImmutableArray.CreateBuilder<BootstrapAssets.AssetPin>();
            for (var i = 0; i < values.Length; i += 3)
            {
                var content = Encoding.UTF8.GetBytes(values[i + 1]);
                File.WriteAllBytes(System.IO.Path.Combine(Path, values[i]), content);
                pins.Add(new(values[i], content.Length, Convert.ToHexString(SHA256.HashData(content)), values[i + 2]));
            }
            return pins.ToImmutable();
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
