// Mirrors: Rust settings.rs schema_rows (keyless rows kept: G-069); the embedded src/Barbican.Core/Settings/settings-schema.json (201 rows in 21 tabs, spec/settings.csv). Contract from the M6 plan section 4.2; owner set-data.

using Barbican.Core.Common;

namespace Barbican.Core.Settings.Pages;

/// <summary>The settings schema as pages and rows.</summary>
public static class SettingsCatalog
{
    /// <summary>Every tab of the schema in order, each row with its spec id (S-key, or S-keyless-NNN in schema order), kind, options and default; keyless rows are kept (G-069). 21 tabs, 201 rows in all.</summary>
    public static IReadOnlyList<SettingPage> Pages() => throw new PortPendingException("SettingsCatalog.Pages");

    /// <summary>The row with this spec id, or null.</summary>
    public static SettingRow? RowOf(string rowId) => throw new PortPendingException("SettingsCatalog.RowOf");
}
