// M6 settings writes and backup snapshots share one mutation gate.
using System.Text.Json.Nodes;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;
using Barbican.Core.Settings.Assistant;
using Barbican.Core.Settings.Backup;
using Barbican.Core.Settings.Pages;
using Barbican.Core.Settings.Prefs;
using Barbican.Core.Storage;
using Barbican.Core.Tests.Settings.Prefs;

namespace Barbican.Core.Tests.Settings.Backup;

public sealed class SettingConcurrencyTests
{
    [Fact]
    public async Task Import_serializes_save_setting_and_export_and_preserves_imported_desktop_data()
    {
        var root = Path.Combine(Path.GetTempPath(), "settings-gate-" + Guid.NewGuid().ToString("N"));
        var prefs = new BlockingPrefs();
        try
        {
            var paths = new TestProfilePaths(root);
            using var gate = new SettingWriteGate();
            var hooks = new HookBus();
            using var backup = new SettingBackup(prefs, prefs, paths, hooks, gate);
            using var values = new SettingValues(prefs, prefs, hooks, gate);
            using var assistant = new AssistantSettings(paths, gate);
            Assert.Equal(AssistantSettings.Defaults.Model, assistant.Config("account-a").Model);
            var document = JsonNode.Parse("""{"app":"barbican","settings":{"theme":"dark"},"desktop":{"other":"kept","assistant":{"account-a":{"model":"imported"}}}}""")!;
            var import = backup.ImportAsync(document, TestContext.Current.CancellationToken);
            await prefs.Entered.Task.WaitAsync(TestContext.Current.CancellationToken);
            var save = assistant.SaveAsync("account-b", AssistantSettings.Defaults with { Model = "new-model" }, TestContext.Current.CancellationToken);
            var setting = values.SetAsync("density", JsonValue.Create("compact"), TestContext.Current.CancellationToken);
            var export = backup.ExportAsync(TestContext.Current.CancellationToken);
            Assert.False(save.IsCompleted);
            Assert.False(setting.IsCompleted);
            Assert.False(export.IsCompleted);
            prefs.Release.TrySetResult();
            Assert.True((await import).IsOk);
            Assert.True((await save).IsOk);
            Assert.True((await setting).IsOk);
            var snapshot = await export;
            Assert.True(snapshot.IsOk);
            Assert.Equal("dark", (string?)snapshot.Value!["settings"]!["theme"]);
            Assert.Equal("kept", (string?)snapshot.Value["desktop"]!["other"]);
            Assert.Equal("imported", assistant.Config("account-a").Model);
            Assert.Equal("new-model", assistant.Config("account-b").Model);
            var disk = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(paths.ConfigDir, "settings.json"), TestContext.Current.CancellationToken))!;
            Assert.Equal("kept", (string?)disk["other"]);
            Assert.Equal("imported", (string?)disk["assistant"]!["account-a"]!["model"]);
            Assert.Equal("new-model", (string?)disk["assistant"]!["account-b"]!["model"]);
        }
        finally
        {
            prefs.Release.TrySetResult();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Concurrent_setting_change_retries_interceptor_with_actual_previous_value()
    {
        var prefs = new BlockingPrefs();
        using var gate = new SettingWriteGate();
        var hooks = new HookBus();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var seen = new List<string?>();
        using var registration = hooks.Intercept<SettingChangePayload>(HookNames.OnBeforeSettingChange, async (payload, ct) =>
        {
            seen.Add((string?)payload.Previous);
            if (seen.Count == 1)
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(ct);
            }

            return true;
        });
        using var first = new SettingValues(prefs, prefs, hooks, gate);
        using var second = new SettingValues(prefs, prefs, new HookBus(), gate);
        var pending = first.SetAsync("density", JsonValue.Create("compact"), TestContext.Current.CancellationToken);
        try
        {
            await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
            Assert.True((await second.SetAsync("density", JsonValue.Create("comfortable"), TestContext.Current.CancellationToken)).IsOk);
            release.TrySetResult();
            Assert.True((await pending).IsOk);
            Assert.Collection(seen, first => Assert.Null(first), second => Assert.Equal("comfortable", second));
            Assert.Equal("compact", (string?)first.Stored("density"));
        }
        finally
        {
            release.TrySetResult();
        }
    }

    [Fact]
    public async Task Catalog_options_are_saved_with_their_expected_types()
    {
        var prefs = new BlockingPrefs();
        using var values = new SettingValues(prefs, prefs, new HookBus());
        Assert.True((await values.SetAsync("fontSize", JsonValue.Create("large"), TestContext.Current.CancellationToken)).IsOk);
        Assert.Equal("large", (string?)values.Stored("fontSize"));
        Assert.True((await values.SetAsync("mailTextScale", JsonValue.Create("1.125"), TestContext.Current.CancellationToken)).IsOk);
        Assert.Equal(1.125, (double?)values.Stored("mailTextScale"));
        Assert.False((await values.SetAsync("fontSize", JsonValue.Create("size"), TestContext.Current.CancellationToken)).IsOk);
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
