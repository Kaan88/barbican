// Assistant settings preserve unrelated desktop settings and isolate accounts.
using System.Text.Json.Nodes;
using Barbican.Core.Settings.Assistant;
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
}
