// Assistant settings preserve unrelated desktop settings and isolate accounts.
using System.Text.Json.Nodes;
using Barbican.Core.Settings.Assistant;
using Barbican.Core.Storage;
using Barbican.Core.Tests.Settings.Prefs;

namespace Barbican.Core.Tests.Settings.Assistant;

public sealed class AssistantSettingsTests
{
    [Fact]
    public async Task Saving_two_accounts_preserves_each_and_other_desktop_settings()
    {
        var root = Path.Combine(Path.GetTempPath(), "barbican-assistant-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new TestProfilePaths(root);
            Directory.CreateDirectory(paths.ConfigDir);
            var file = Path.Combine(paths.ConfigDir, "settings.json");
            await File.WriteAllTextAsync(file, """{"other":"kept"}""", TestContext.Current.CancellationToken);
            var settings = new AssistantSettings(paths);
            Assert.True((await settings.SaveAsync("a", AssistantSettings.Defaults with { Model = "first-model" }, TestContext.Current.CancellationToken)).IsOk);
            Assert.True((await settings.SaveAsync("b", AssistantSettings.Defaults with { Model = "second-model" }, TestContext.Current.CancellationToken)).IsOk);
            var reopened = new AssistantSettings(paths);
            await reopened.InitializeAsync(TestContext.Current.CancellationToken);
            Assert.Equal("first-model", reopened.Config("a").Model);
            Assert.Equal("second-model", reopened.Config("b").Model);
            Assert.Equal("kept", (string?)JsonNode.Parse(await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken))?["other"]);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
    [Fact]
    public void Config_is_a_memory_read_even_before_initialization()
    {
        using var settings = new AssistantSettings(new NoFilePaths());
        Assert.Equal(AssistantSettings.Defaults.Model, settings.Config("a").Model);
    }

    [Fact]
    public async Task Config_does_not_wait_for_slow_profile_load()
    {
        using var paths = new BlockingPaths();
        using var settings = new AssistantSettings(paths);
        var loading = settings.InitializeAsync(TestContext.Current.CancellationToken);
        try
        {
            await paths.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            var read = Task.Run(() => settings.Config("a"), TestContext.Current.CancellationToken);
            var config = await read.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(AssistantSettings.Defaults.Model, config.Model);
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
        public string ConfigDir => throw new InvalidOperationException("Config must not access profile files.");
        public string DataDir => throw new InvalidOperationException("Config must not access profile files.");
    }

}
