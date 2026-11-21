// The per-account settings of an add-on preserve unrelated desktop settings, isolate accounts, adopt the section an add-on had before add-ons existed and never wait for the disk (B-SET-039, D-M6-A13, decision D-M6b-08).
using System.Text.Json.Nodes;
using Barbican.Core.Plugins.Native.Host;
using Barbican.Core.Storage;
using Barbican.Core.Tests.Settings.Prefs;

namespace Barbican.Core.Tests.Plugins.Native.Host;

public sealed class PluginSettingsStoreTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Saving_two_accounts_preserves_each_and_other_desktop_settings()
    {
        var root = Path.Combine(Path.GetTempPath(), "barbican-addon-settings-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new TestProfilePaths(root);
            Directory.CreateDirectory(paths.ConfigDir);
            var file = Path.Combine(paths.ConfigDir, "settings.json");
            await File.WriteAllTextAsync(file, """{"other":"kept"}""", Ct);
            using var settings = new PluginSettingsStore(paths, "assistant");
            await settings.SetAsync("a", new Dictionary<string, string?>(StringComparer.Ordinal) { ["model"] = "first-model" }, Ct);
            await settings.SetAsync("b", new Dictionary<string, string?>(StringComparer.Ordinal) { ["model"] = "second-model" }, Ct);
            using var reopened = new PluginSettingsStore(paths, "assistant");
            await reopened.InitializeAsync(Ct);
            Assert.Equal("first-model", reopened.Read("a", "model"));
            Assert.Equal("second-model", reopened.Read("b", "model"));
            Assert.Equal("kept", (string?)JsonNode.Parse(await File.ReadAllTextAsync(file, Ct))?["other"]);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Another_add_on_does_not_see_the_values()
    {
        var root = Path.Combine(Path.GetTempPath(), "barbican-addon-isolation-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new TestProfilePaths(root);
            using var gate = new Barbican.Core.Settings.Prefs.SettingWriteGate();
            using var mine = new PluginSettingsStore(paths, "mine", gate);
            using var other = new PluginSettingsStore(paths, "other", gate);
            await mine.SetAsync("a", new Dictionary<string, string?>(StringComparer.Ordinal) { ["key"] = "value" }, Ct);
            Assert.Equal("value", mine.Read("a", "key"));
            Assert.Null(other.Read("a", "key"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task A_section_from_before_add_ons_is_read_unchanged_and_copied_on_the_first_save()
    {
        var root = Path.Combine(Path.GetTempPath(), "barbican-addon-legacy-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new TestProfilePaths(root);
            Directory.CreateDirectory(paths.ConfigDir);
            var file = Path.Combine(paths.ConfigDir, "settings.json");
            await File.WriteAllTextAsync(file, """{"assistant":{"a":{"provider":1,"memory":false,"model":"m","notes":["likes short answers"]}}}""", Ct);
            using var settings = new PluginSettingsStore(paths, "assistant");
            await settings.InitializeAsync(Ct);
            Assert.Equal("1", settings.Read("a", "provider"));
            Assert.Equal("false", settings.Read("a", "memory"));
            Assert.Equal("""["likes short answers"]""", settings.Read("a", "notes"));
            await settings.SetAsync("a", new Dictionary<string, string?>(StringComparer.Ordinal) { ["model"] = "new" }, Ct);
            var disk = JsonNode.Parse(await File.ReadAllTextAsync(file, Ct))!;
            Assert.Equal("new", (string?)disk["addons"]!["assistant"]!["a"]!["model"]);
            Assert.Equal("1", disk["addons"]!["assistant"]!["a"]!["provider"]!.ToJsonString());
            Assert.Equal("m", (string?)disk["assistant"]!["a"]!["model"]);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Read_is_a_memory_read_even_before_initialization()
    {
        using var settings = new PluginSettingsStore(new NoFilePaths(), "assistant");
        Assert.Null(settings.Read("a", "model"));
    }

    [Fact]
    public async Task Read_does_not_wait_for_slow_profile_load()
    {
        using var paths = new BlockingPaths();
        using var settings = new PluginSettingsStore(paths, "assistant");
        var loading = settings.InitializeAsync(Ct);
        try
        {
            await paths.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
            var read = Task.Run(() => settings.Read("a", "model"), Ct);
            Assert.Null(await read.WaitAsync(TimeSpan.FromSeconds(5), Ct));
        }
        finally
        {
            paths.Release.Set();
        }

        await loading;
    }

    private sealed class BlockingPaths : IProfilePaths, IDisposable
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Release { get; } = new(false);
        public string Profile => "test";
        public string ConfigDir
        {
            get
            {
                Entered.TrySetResult();
                Release.Wait();
                return Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid().ToString("N"));
            }
        }

        public string DataDir => throw new NotSupportedException();
        public void Dispose() => Release.Dispose();
    }

    private sealed class NoFilePaths : IProfilePaths
    {
        public string Profile => "test";
        public string ConfigDir => throw new InvalidOperationException("Read must not access profile files.");
        public string DataDir => throw new InvalidOperationException("Read must not access profile files.");
    }
}
