// Mirrors: M6 plan section 4.2 (settings pages as data); Bulwark components/settings/settings-app.tsx, settings-section.tsx; Rust settings.rs schema_rows, ui/settings-schema.json (extracted by tools/extract_settings.py). Contract from the M6 plan; owner set-data.

namespace Barbican.Core.Settings.Pages;

/// <summary>A search result; the screen opens <c>PageId</c> with <c>RowId</c> as the anchor.</summary>
public sealed record SettingSearchHit(string PageId, string RowId, string Label, string Description, double Score);
