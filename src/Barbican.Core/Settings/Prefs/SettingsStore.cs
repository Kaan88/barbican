// Mirrors: Rust settings.rs:159-:190 (export, import, reset), :105 load; Bulwark settings export format. Stub from F2; owner accounts.

using Barbican.Core.Common;
using System.Text.Json.Nodes;
using Barbican.Core.Storage;

// CA1822: instance members of a stub that no stream has implemented yet; each gains state when its stream lands.
#pragma warning disable CA1822

namespace Barbican.Core.Settings.Prefs;

/// <summary>The mutable settings: prefs.json plus the desktop file (<c>settings.json</c>) in the profile's config dir.</summary>
public sealed class SettingsStore
{
    private SettingsStore()
    {
    }

    /// <summary>The Bulwark-keyed prefs.</summary>
    public JsonObject Prefs { get; } = new();

    /// <summary>Loads the settings of a profile (empty when nothing is stored yet).</summary>
    public static SettingsStore Load(IProfilePaths paths) => throw new PortPendingException("SettingsStore.Load: accounts stream");

    /// <summary>A string pref, or the default.</summary>
    public string Str(string key, string fallback) => throw new PortPendingException("SettingsStore.Str: accounts stream");

    /// <summary>A boolean pref, or the default.</summary>
    public bool Bool(string key, bool fallback) => throw new PortPendingException("SettingsStore.Bool: accounts stream");

    /// <summary>
    /// Imports a Bulwark export (flat, or wrapped in <c>settings</c>) or a Barbican export (with the <c>desktop</c> file).
    /// Returns false with a message for anything that is not a JSON object; the prefs stay as they were.
    /// </summary>
    public bool TryImport(JsonNode document, out string error) => throw new PortPendingException("SettingsStore.TryImport: accounts stream");

    /// <summary>Everything worth backing up: <c>{app, version, settings, desktop}</c>.</summary>
    public JsonObject Export() => throw new PortPendingException("SettingsStore.Export: accounts stream");

    /// <summary>Clears the prefs and removes the desktop file.</summary>
    public void Reset() => throw new PortPendingException("SettingsStore.Reset: accounts stream");
}
