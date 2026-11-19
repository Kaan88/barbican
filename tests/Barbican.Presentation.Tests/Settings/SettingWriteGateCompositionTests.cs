// The production settings registrations must share the same mutation gate.
using System.Text.Json.Nodes;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Settings;
using Barbican.Core.Settings.Assistant;
using Barbican.Core.Settings.Backup;
using Barbican.Core.Settings.Pages;
using Barbican.Core.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Barbican.Presentation.Tests.Settings;

public sealed class SettingWriteGateCompositionTests
{
    [Fact]
    public async Task Registered_services_wait_for_an_import_to_finish()
    {
        var root = Path.Combine(Path.GetTempPath(), "settings-di-" + Guid.NewGuid().ToString("N"));
        var prefs = new BlockingPrefs();
        try
        {
            var services = new ServiceCollection();
            services.AddSingleton<IPrefsStore>(prefs);
            services.AddSingleton<IPrefsEditor>(prefs);
            services.AddSingleton<IProfilePaths>(new Paths(root));
            services.AddSingleton<IHookBus>(new HookBus());
            services.AddBarbicanSettingsData();
            using var provider = services.BuildServiceProvider();
            var backup = provider.GetRequiredService<ISettingBackup>();
            var values = provider.GetRequiredService<ISettingValues>();
            var assistant = provider.GetRequiredService<IAssistantSettings>();
            var import = backup.ImportAsync(JsonNode.Parse("""{"app":"barbican","settings":{"theme":"dark"},"desktop":{"other":"kept"}}""")!, TestContext.Current.CancellationToken);
            await prefs.Entered.Task.WaitAsync(TestContext.Current.CancellationToken);
            var save = assistant.SaveAsync("a", AssistantSettings.Defaults with { Model = "new-model" }, TestContext.Current.CancellationToken);
            var set = values.SetAsync("density", JsonValue.Create("compact"), TestContext.Current.CancellationToken);
            var export = backup.ExportAsync(TestContext.Current.CancellationToken);
            Assert.False(save.IsCompleted);
            Assert.False(set.IsCompleted);
            Assert.False(export.IsCompleted);
            prefs.Release.TrySetResult();
            Assert.True((await import).IsOk);
            Assert.True((await save).IsOk);
            Assert.True((await set).IsOk);
            Assert.Equal("kept", (string?)(await export).Value!["desktop"]!["other"]);
        }
        finally
        {
            prefs.Release.TrySetResult();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class Paths(string root) : IProfilePaths
    {
        public string Profile => "test";
        public string ConfigDir => Path.Combine(root, "config");
        public string DataDir => Path.Combine(root, "data");
    }

    private sealed class BlockingPrefs : IPrefsStore, IPrefsEditor
    {
        private readonly JsonObject _values = [];
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public PrefsSnapshot Current => PrefsSnapshot.FromJson((JsonObject)_values.DeepClone());
        public event EventHandler? Changed { add { } remove { } }
        public void Put(string key, JsonNode? value)
        {
            if (value is null) _values.Remove(key);
            else _values[key] = value.DeepClone();
        }

        public async Task FlushAsync(CancellationToken ct)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(ct);
        }
    }
}
