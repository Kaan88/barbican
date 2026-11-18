// Mirrors: M6 plan section 4.2 (settings pages as data); Bulwark components/settings/settings-app.tsx, settings-section.tsx; Rust settings.rs schema_rows, ui/settings-schema.json (extracted by tools/extract_settings.py). Contract from the M6 plan; owner set-data.

namespace Barbican.Core.Settings.Pages;

/// <summary>How a row is backed.</summary>
public enum SettingRowStatus
{
    /// <summary>The key is read by the app (spec/settings.csv `wired`).</summary>
    Wired,

    /// <summary>The key exists in Bulwark but nothing read it in Rust (`unwired`): M6 wires it or marks it inert with a reason (D-M6-A03).</summary>
    Unwired,

    /// <summary>No key (`keyless`): the row is a handler (a button, a page section) or an info provider (<see cref="ISettingInfo"/>).</summary>
    Keyless,
}
