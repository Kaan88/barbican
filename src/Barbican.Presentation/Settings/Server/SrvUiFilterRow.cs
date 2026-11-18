// Mirrors: Bulwark components/settings/filter-settings.tsx, folder-settings.tsx, vacation-settings.tsx, M6 plan section 4.11. Contract from the M6 plan; owner srv-ui.

namespace Barbican.Presentation.Settings.Server;

/// <summary>A rule in the filters list (Bulwark filter-settings.tsx).</summary>
public sealed record SrvUiFilterRow(string Id, string Name, string Summary, bool Enabled);
