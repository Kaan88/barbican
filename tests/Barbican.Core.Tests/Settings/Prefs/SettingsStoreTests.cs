// Ported from Rust src/settings.rs test (:581). Red until the accounts stream implements SettingsStore.

using System.Text.Json.Nodes;
using Barbican.Core.Settings.Prefs;

namespace Barbican.Core.Tests.Settings.Prefs;

public sealed class SettingsStoreTests
{
    private static JsonNode Json(string text) => JsonNode.Parse(text)!;

    // One test, as in Rust, so the shared directory cannot race another test.
    [Fact]
    [RustTest("settings::import_export_reset_roundtrip")]
    [Red("accounts")]
    public void Import_export_reset_roundtrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"barbican-settings-test-{Environment.ProcessId}");
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }

        try
        {
            var paths = new TestProfilePaths(dir);
            var desktopFile = Path.Combine(paths.ConfigDir, "settings.json");
            var s = SettingsStore.Load(paths);

            // Bulwark-shaped: flat settings object.
            Assert.True(s.TryImport(Json("""{"theme": "dark", "showPreview": false}"""), out _));
            Assert.Equal("dark", s.Str("theme", ""));
            Assert.False(s.Bool("showPreview", true));
            Assert.False(File.Exists(desktopFile));

            // Bulwark-shaped with a "settings" wrapper.
            Assert.True(s.TryImport(Json("""{"version": 2, "settings": {"theme": "light"}}"""), out _));
            Assert.Equal("light", s.Str("theme", ""));
            Assert.False(s.Prefs.ContainsKey("showPreview"));

            // Barbican export, including the desktop file.
            Assert.True(s.TryImport(Json("""{"app": "barbican", "version": 1, "settings": {"density": "compact"}, "desktop": {"ai_model": "x"}}"""), out _));
            Assert.Equal("compact", s.Str("density", ""));
            var desktop = JsonNode.Parse(File.ReadAllText(desktopFile))!;
            Assert.Equal("x", desktop["ai_model"]!.GetValue<string>());
            var reloaded = SettingsStore.Load(paths);
            Assert.Equal("compact", reloaded.Str("density", ""));
            var export = s.Export();
            Assert.Equal("barbican", export["app"]!.GetValue<string>());
            Assert.Equal("compact", export["settings"]!["density"]!.GetValue<string>());
            Assert.Equal("x", export["desktop"]!["ai_model"]!.GetValue<string>());

            // Rejects non-objects.
            Assert.False(s.TryImport(Json("[1, 2]"), out _));
            Assert.False(s.TryImport(Json("\"nope\""), out _));
            Assert.Equal("compact", s.Str("density", ""));

            s.Reset();
            Assert.Empty(s.Prefs);
            Assert.False(File.Exists(desktopFile));
            Assert.Empty(SettingsStore.Load(paths).Prefs);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
