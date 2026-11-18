// Mirrors: M6 plan section 4.2 (settings pages as data); Bulwark components/settings/settings-app.tsx, settings-section.tsx; Rust settings.rs schema_rows, ui/settings-schema.json (extracted by tools/extract_settings.py). Contract from the M6 plan; owner set-data.

namespace Barbican.Core.Settings.Pages;

/// <summary>One tab of the schema with its rows in order.</summary>
public sealed record SettingPage(string Id, string Label, string Group, IReadOnlyList<SettingRow> Rows);
