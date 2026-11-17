// Mirrors: Rust settings.rs:105 load, :160 export, :167 import, :178 reset, :191 bool, :195 str (at rust-final); Bulwark settings export
// format (a flat settings object, or one wrapped in "settings"); spec B-SET-006, test T-259. The synchronous file side of settings: backup,
// restore and reset (the settings pages are M6). The running app reads and changes prefs through PrefsStore; both write prefs.json atomically.

using System.Text.Json;
using System.Text.Json.Nodes;
using Barbican.Core.Storage;

namespace Barbican.Core.Settings.Prefs;

/// <summary>The mutable settings: prefs.json plus the desktop file (<c>settings.json</c>) in the profile's config dir.</summary>
public sealed class SettingsStore
{
    /// <summary>The desktop and assistant settings file (Rust settings.rs:161).</summary>
    public const string DesktopFileName = "settings.json";

    private readonly IProfilePaths _paths;

    private SettingsStore(IProfilePaths paths, JsonObject prefs)
    {
        _paths = paths;
        Prefs = prefs;
    }

    /// <summary>The Bulwark-keyed prefs.</summary>
    public JsonObject Prefs { get; private set; }

    private string PrefsPath => Path.Combine(_paths.ConfigDir, PrefsStore.FileName);

    private string DesktopPath => Path.Combine(_paths.ConfigDir, DesktopFileName);

    /// <summary>Loads the settings of a profile (empty when nothing is stored yet).</summary>
    public static SettingsStore Load(IProfilePaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return new SettingsStore(paths, ReadObject(Path.Combine(paths.ConfigDir, PrefsStore.FileName)) ?? []);
    }

    /// <summary>A string pref, or the default.</summary>
    public string Str(string key, string fallback) => PrefsSnapshot.FromJson(Prefs).Str(key, fallback);

    /// <summary>A boolean pref, or the default.</summary>
    public bool Bool(string key, bool fallback) =>
        Prefs[key] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : fallback;

    /// <summary>
    /// Imports a Bulwark export (flat, or wrapped in <c>settings</c>) or a Barbican export (with the <c>desktop</c> file).
    /// Returns false with a message for anything that is not a JSON object; the prefs stay as they were.
    /// </summary>
    public bool TryImport(JsonNode document, out string error)
    {
        ArgumentNullException.ThrowIfNull(document);
        var root = document as JsonObject;
        var isBarbican = root?["app"] is JsonValue app && app.TryGetValue<string>(out var name) && string.Equals(name, "barbican", StringComparison.Ordinal);
        var prefs = isBarbican || root?["settings"] is JsonObject ? root!["settings"] : document;
        if (prefs is not JsonObject obj)
        {
            error = "This file has no settings.";
            return false;
        }

        Prefs = (JsonObject)obj.DeepClone();
        Save();
        if (root?["desktop"] is JsonObject desktop)
        {
            AtomicFile.WriteAllText(DesktopPath, desktop.ToJsonString());
        }

        error = string.Empty;
        return true;
    }

    /// <summary>Everything worth backing up: <c>{app, version, settings, desktop}</c>.</summary>
    public JsonObject Export() => new()
    {
        ["app"] = "barbican",
        ["version"] = 1,
        ["settings"] = Prefs.DeepClone(),
        ["desktop"] = ReadObject(DesktopPath) ?? [],
    };

    /// <summary>Clears the prefs and removes the desktop file.</summary>
    public void Reset()
    {
        Prefs = [];
        Save();
        AtomicFile.Delete(DesktopPath);
    }

    private static JsonObject? ReadObject(string path)
    {
        var text = AtomicFile.ReadAllTextOrNull(path);
        if (text is null)
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(text) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void Save() => AtomicFile.WriteAllText(PrefsPath, Prefs.ToJsonString());
}
