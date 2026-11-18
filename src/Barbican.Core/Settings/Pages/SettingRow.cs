// Mirrors: M6 plan section 4.2 (settings pages as data); Bulwark components/settings/settings-app.tsx, settings-section.tsx; Rust settings.rs schema_rows, ui/settings-schema.json (extracted by tools/extract_settings.py). Contract from the M6 plan; owner set-data.

using System.Text.Json.Nodes;

namespace Barbican.Core.Settings.Pages;

/// <summary>One row of a settings page. <c>Id</c> is the spec id (<c>S-colorBlindPalette</c>, <c>S-keyless-012</c>); <c>Key</c> the Bulwark pref key (null for a keyless row); <c>Section</c> the label of the section row above it.</summary>
public sealed record SettingRow(
    string Id,
    string Page,
    SettingRowKind Kind,
    string Label,
    string Description,
    string? Key,
    IReadOnlyList<SettingOption> Options,
    JsonNode? Default,
    SettingRowStatus Status,
    bool Advanced,
    string? Section,
    bool Inverted = false);
