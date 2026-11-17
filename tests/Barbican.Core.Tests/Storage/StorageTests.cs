// Profile folders (PORT-PLAN.md §3.5, ADR 0007/0008: Barbican-<profile> beside the Rust app's Barbican), atomic writes, prefs.json kept as a
// JSON object with unknown keys (Rust settings.rs:35 save keeps the whole object; Bulwark settings-store persists only what it knows, which
// is what this avoids), the debounced save, the dev profile's one-time copy from the Rust app, and the UI-thread IO check.

using System.Text.Json.Nodes;
using Barbican.Core.Storage;
using Barbican.Core.Tests.Settings.Prefs;
using Microsoft.Extensions.Time.Testing;

namespace Barbican.Core.Tests.Storage;

public sealed class StorageTests : IDisposable
{
    [ThreadStatic]
    private static bool t_ui;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "barbican-storage-" + Guid.NewGuid().ToString("N"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    [Fact]
    public void Profiles_sit_beside_the_rust_folder_and_never_in_it()
    {
        var env = new Dictionary<string, string?>(StringComparer.Ordinal) { ["APPDATA"] = @"X:\Roaming", ["LOCALAPPDATA"] = @"X:\Local" };
        var win = ProfilePaths.Resolve(null, windows: true, k => env.GetValueOrDefault(k), @"X:\home");
        Assert.Equal("dev", win.Profile);
        Assert.Equal(Path.Combine(@"X:\Roaming", "Barbican-dev"), win.ConfigDir);
        Assert.Equal(Path.Combine(@"X:\Local", "Barbican-dev"), win.DataDir);
        Assert.Equal(Path.Combine(@"X:\Roaming", "Barbican"), ProfilePaths.RustConfigDir(true, k => env.GetValueOrDefault(k), @"X:\home"));

        var linux = ProfilePaths.Resolve("auto-9123", windows: false, _ => null, "/home/someone");
        Assert.Equal(Path.Combine("/home/someone", ".config", "barbican-auto-9123"), linux.ConfigDir);
        Assert.True(linux.IsAutomation);
        var xdg = ProfilePaths.Resolve("x", windows: false, k => string.Equals(k, "XDG_CONFIG_HOME", StringComparison.Ordinal) ? "relative/ignored" : null, "/h");
        Assert.Equal(Path.Combine("/h", ".config", "barbican-x"), xdg.ConfigDir);
    }

    [Theory]
    [InlineData("dev", true)]
    [InlineData("auto-1234", true)]
    [InlineData("a", true)]
    [InlineData("", false)]
    [InlineData("-x", false)]
    [InlineData("Dev", false)]
    [InlineData("../up", false)]
    [InlineData("a b", false)]
    public void Profile_names_are_checked(string name, bool ok)
    {
        Assert.Equal(ok, ProfilePaths.IsValidName(name));
        if (!ok && name.Length > 0)
        {
            Assert.Throws<ArgumentException>(() => ProfilePaths.Resolve(name, windows: true, _ => null, "h"));
        }
    }

    [Fact]
    public void Atomic_writes_replace_and_leave_no_temp_files()
    {
        var path = Path.Combine(_root, "sub", "f.json");
        AtomicFile.WriteAllText(path, "one");
        AtomicFile.WriteAllText(path, "two");
        Assert.Equal("two", AtomicFile.ReadAllTextOrNull(path));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!));
        Assert.Null(AtomicFile.ReadAllTextOrNull(Path.Combine(_root, "missing.json")));
        AtomicFile.Delete(path);
        AtomicFile.Delete(path);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Prefs_round_trip_keeps_keys_this_build_does_not_know()
    {
        var paths = new TestProfilePaths(_root);
        Directory.CreateDirectory(paths.ConfigDir);
        await File.WriteAllTextAsync(Path.Combine(paths.ConfigDir, PrefsStore.FileName), """{"theme":"dark","future_key":{"nested":[1,2,3]},"rust_only":true}""", Ct);

        using (var prefs = new PrefsStore(paths, TimeProvider.System))
        {
            await prefs.WaitLoadedAsync(Ct);
            Assert.Equal("dark", prefs.Current.Str("theme", "light"));
            prefs.Put("theme", JsonValue.Create("light"));
            prefs.Put("rust_only", null);
            await prefs.FlushAsync(Ct);
        }

        var saved = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(paths.ConfigDir, PrefsStore.FileName), Ct))!.AsObject();
        Assert.Equal("light", saved["theme"]!.GetValue<string>());
        Assert.Equal(3, saved["future_key"]!["nested"]!.AsArray().Count);
        Assert.False(saved.ContainsKey("rust_only"));

        using var again = new PrefsStore(paths, TimeProvider.System);
        Assert.Equal("light", again.Current.Str("theme", "dark"));
    }

    [Fact]
    public async Task Prefs_saves_are_debounced()
    {
        var paths = new TestProfilePaths(_root);
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.Zero));
        using var prefs = new PrefsStore(paths, clock);
        await prefs.WaitLoadedAsync(Ct);
        var file = Path.Combine(paths.ConfigDir, PrefsStore.FileName);
        for (var i = 0; i < 20; i++)
        {
            prefs.Put("n", JsonValue.Create(i));
        }

        Assert.False(File.Exists(file));
        clock.Advance(PrefsStore.SaveDelay);
        await Barbican.Core.Tests.Jmap.Poll.UntilAsync(() => File.Exists(file) && File.ReadAllText(file).Contains("19", StringComparison.Ordinal), "the debounced save");
        Assert.Equal(19, JsonNode.Parse(await File.ReadAllTextAsync(file, Ct))!["n"]!.GetValue<int>());
    }

    [Fact]
    public async Task A_broken_prefs_file_reads_as_empty_and_is_not_overwritten_until_a_change()
    {
        var paths = new TestProfilePaths(_root);
        Directory.CreateDirectory(paths.ConfigDir);
        var file = Path.Combine(paths.ConfigDir, PrefsStore.FileName);
        await File.WriteAllTextAsync(file, "{not json", Ct);
        using var prefs = new PrefsStore(paths, TimeProvider.System);
        Assert.Equal("light", prefs.Current.Str("theme", "light"));
        Assert.Equal("{not json", await File.ReadAllTextAsync(file, Ct));
    }

    [Fact]
    public void The_dev_profile_copies_the_rust_prefs_once_and_never_writes_them()
    {
        var rust = Path.Combine(_root, "rust");
        Directory.CreateDirectory(rust);
        File.WriteAllText(Path.Combine(rust, "prefs.json"), """{"theme":"dark"}""");
        File.WriteAllText(Path.Combine(rust, "search-history.json"), "[\"from:a\"]");
        var before = Directory.GetFiles(rust).Select(f => (f, File.GetLastWriteTimeUtc(f))).ToList();

        var dev = ProfilePaths.At("dev", Path.Combine(_root, "dev", "config"), Path.Combine(_root, "dev", "data"));
        Assert.Equal(["prefs.json", "search-history.json"], RustProfileCopy.Run(dev, rust));
        Assert.Equal("""{"theme":"dark"}""", File.ReadAllText(Path.Combine(dev.ConfigDir, "prefs.json")));

        // later runs copy nothing, even after the user deleted a copied file
        File.Delete(Path.Combine(dev.ConfigDir, "prefs.json"));
        Assert.Empty(RustProfileCopy.Run(dev, rust));
        Assert.Equal(before, Directory.GetFiles(rust).Select(f => (f, File.GetLastWriteTimeUtc(f))).ToList());

        // other profiles never copy
        var other = ProfilePaths.At("auto-1", Path.Combine(_root, "auto", "config"), Path.Combine(_root, "auto", "data"));
        Assert.Empty(RustProfileCopy.Run(other, rust));
        Assert.False(Directory.Exists(other.ConfigDir));
    }

    [Fact]
    public void Profile_io_on_the_ui_thread_is_counted()
    {
        IoThreadCheck.AssertOnViolation = false;
        IoThreadCheck.Install(() => t_ui);
        try
        {
            var before = IoThreadCheck.Violations;
            IoThreadCheck.AssertNotUiThread();
            Assert.Equal(before, IoThreadCheck.Violations);
            t_ui = true;
            IoThreadCheck.AssertNotUiThread();
            Assert.Equal(before + 1, IoThreadCheck.Violations);
        }
        finally
        {
            t_ui = false;
            IoThreadCheck.Install(null);
            IoThreadCheck.AssertOnViolation = true;
        }
    }
}
