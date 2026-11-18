// Mirrors: M6 plan section 4.2 (settings pages as data); Bulwark components/settings/settings-app.tsx, settings-section.tsx; Rust settings.rs schema_rows, ui/settings-schema.json (extracted by tools/extract_settings.py). Contract from the M6 plan; owner set-data.

namespace Barbican.Core.Settings.Pages;

/// <summary>What a row of a settings page shows (the schema's `kind`; a `section` row starts a titled group).</summary>
public enum SettingRowKind
{
    Toggle,
    Select,
    Text,
    Button,
    Info,
    Section,
}
