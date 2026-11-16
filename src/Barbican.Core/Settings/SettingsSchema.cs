// Mirrors: Rust ui/settings-schema.json (extracted from Bulwark's settings pages by
// tools/extract_settings.py); Bulwark stores/settings-store.ts for the keys.
// The schema is embedded verbatim and drives the settings pages (PORT-PLAN.md §2.2, M6).

namespace Barbican.Core.Settings;

/// <summary>Access to the embedded settings schema (the same JSON the Rust app used).</summary>
public static class SettingsSchema
{
    internal const string ResourceName = "Barbican.Core.Settings.settings-schema.json";

    /// <summary>Opens the schema JSON. The caller disposes the stream.</summary>
    public static Stream OpenRaw()
    {
        var stream = typeof(SettingsSchema).Assembly.GetManifestResourceStream(ResourceName);
        return stream ?? throw new InvalidOperationException($"Embedded resource {ResourceName} is missing.");
    }
}
