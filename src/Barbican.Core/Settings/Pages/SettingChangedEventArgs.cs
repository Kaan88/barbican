// Mirrors: M6 plan section 4.2 (settings pages as data); Bulwark components/settings/settings-app.tsx, settings-section.tsx; Rust settings.rs schema_rows, ui/settings-schema.json (extracted by tools/extract_settings.py). Contract from the M6 plan; owner set-data.

using System.Text.Json.Nodes;

namespace Barbican.Core.Settings.Pages;

/// <summary>A setting was written.</summary>
public sealed class SettingChangedEventArgs(string key, JsonNode? value) : EventArgs
{
    public string Key { get; } = key;

    /// <summary>The new value; null when the key was reset to its default.</summary>
    public JsonNode? Value { get; } = value;
}
