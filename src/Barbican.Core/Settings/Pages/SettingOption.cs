// Mirrors: M6 plan section 4.2 (settings pages as data); Bulwark components/settings/settings-app.tsx, settings-section.tsx; Rust settings.rs schema_rows, ui/settings-schema.json (extracted by tools/extract_settings.py). Contract from the M6 plan; owner set-data.

namespace Barbican.Core.Settings.Pages;

/// <summary>One choice of a select row.</summary>
public sealed record SettingOption(string Value, string Label);
