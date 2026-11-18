// Mirrors: M6 plan section 4.2 (settings pages as data); Bulwark components/settings/settings-app.tsx, settings-section.tsx; Rust settings.rs schema_rows, ui/settings-schema.json (extracted by tools/extract_settings.py). Contract from the M6 plan; owner set-data.

using System.Text.Json.Nodes;

namespace Barbican.Core.Settings.Pages;

/// <summary>The outcome of checking a value against its row: the coerced value, or what is wrong.</summary>
public sealed record SettingCheck(bool Ok, JsonNode? Value, string? Error);
