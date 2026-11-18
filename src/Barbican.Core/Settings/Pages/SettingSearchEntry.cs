// Mirrors: M6 plan section 4.2 (settings pages as data); Bulwark components/settings/settings-app.tsx, settings-section.tsx; Rust settings.rs schema_rows, ui/settings-schema.json (extracted by tools/extract_settings.py). Contract from the M6 plan; owner set-data.

namespace Barbican.Core.Settings.Pages;

/// <summary>One searchable thing: a catalog row, or a whole page of the server-backed pages (RowId empty) with its keywords.</summary>
public sealed record SettingSearchEntry(string PageId, string RowId, string Label, string Description, string Keywords);
