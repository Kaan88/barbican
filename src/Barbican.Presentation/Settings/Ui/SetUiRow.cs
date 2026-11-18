// Mirrors: Bulwark components/settings/settings-app.tsx, settings-section.tsx (SettingItem rows), M6 plan section 4.10. Contract from the M6 plan; owner set-ui.

using System.Text.Json.Nodes;
using Barbican.Core.Settings.Pages;

namespace Barbican.Presentation.Settings.Ui;

/// <summary>A row as the screen draws it: the catalog row, its effective value (stored, else the default), the text of an info row, and whether the current filters show it.</summary>
public sealed record SetUiRow(SettingRow Row, JsonNode? Value, string InfoText, bool Visible);
