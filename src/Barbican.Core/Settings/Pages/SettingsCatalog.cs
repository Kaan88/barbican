// Mirrors: Rust settings.rs schema_rows (keyless rows kept: G-069); the embedded src/Barbican.Core/Settings/settings-schema.json (276 rows in 21 tabs: 74 sections and the 202 rows of spec/settings.csv less one key that two tabs share). Bulwark components/settings/settings-app.tsx (the tabs), settings-section.tsx (row kinds, `invert`). Contract from the M6 plan section 4.2; owner set-data.
// Decisions D-M6-A01 (ids), D-M6-A03 (unwired keys), D-M6-A10 (advanced rows).

using System.Globalization;
using System.Text.Json.Nodes;

namespace Barbican.Core.Settings.Pages;

/// <summary>The settings schema as pages and rows.</summary>
public static class SettingsCatalog
{
    // keys nothing read in Rust (spec/settings.csv status `unwired`, D-M6-A03)
    private static readonly HashSet<string> UnwiredKeys = new(StringComparer.Ordinal)
    {
        "settingsSyncDisabled", "compactSharedMailboxes", "keepAppsLoaded", "mailLayout", "proInterface", "showRailAccountList", "showSharedMailAccounts", "sidebarAccountSwitcher",
        "toolbarPosition", "mentionAddsRecipientTo", "recipientMentionsEnabled", "rtlEditingSupport", "filenameCollapseSeparators", "filenameLowercase", "filenameSpaceReplacement",
        "filenameStripDiacritics", "postExportAction", "dateLocale", "showOnboardingOnNewDevices", "listColumns", "messageListOrderScope", "swipeLeftAction", "swipeRightAction",
        "notificationSoundChoice", "pushRelayUrl", "debugMode",
    };

    // rows only a user who knows the server or the protocol needs (D-M6-A10): shown with "Show advanced settings"
    private static readonly HashSet<string> AdvancedKeys = new(StringComparer.Ordinal)
    {
        "debugMode", "pushRelayUrl", "subAddressDelimiter", "dateLocale", "listColumns", "messageListOrderScope", "keepAppsLoaded", "proInterface",
    };

    private static readonly Lazy<(IReadOnlyList<SettingPage> Pages, IReadOnlyDictionary<string, SettingRow> Rows)> Parsed = new(Parse);

    /// <summary>Every tab of the schema in order, each row with its spec id (S-key, or S-keyless-NNN in schema order), kind, options and default; keyless rows are kept (G-069). 21 tabs, 276 rows in all (74 of them section rows).</summary>
    public static IReadOnlyList<SettingPage> Pages() => Parsed.Value.Pages;

    /// <summary>The row with this spec id, or null.</summary>
    public static SettingRow? RowOf(string rowId) => rowId is not null && Parsed.Value.Rows.TryGetValue(rowId, out var row) ? row : null;

    /// <summary>The row of a pref key (the first page that has it), or null.</summary>
    public static SettingRow? RowOfKey(string key) => key is not null && Parsed.Value.Rows.TryGetValue("S-" + key, out var row) ? row : null;

    private static (IReadOnlyList<SettingPage>, IReadOnlyDictionary<string, SettingRow>) Parse()
    {
        using var stream = SettingsSchema.OpenRaw();
        var root = JsonNode.Parse(stream) as JsonObject ?? throw new InvalidOperationException("The settings schema is not a JSON object.");
        var pages = new List<SettingPage>();
        var rows = new Dictionary<string, SettingRow>(StringComparer.Ordinal);
        var keyless = 0;
        foreach (var tab in root["tabs"] as JsonArray ?? [])
        {
            var id = (string)tab!["id"]!;
            var list = new List<SettingRow>();
            var sections = 0;
            string? section = null;
            foreach (var node in tab["rows"] as JsonArray ?? [])
            {
                var o = node!.AsObject();
                var kind = KindOf((string)o["kind"]!);
                var label = (string?)o["label"] ?? string.Empty;
                var desc = (string?)o["desc"] ?? string.Empty;
                var key = (string?)o["key"];
                SettingRow row;
                if (kind == SettingRowKind.Section)
                {
                    section = label;
                    row = new SettingRow(string.Create(CultureInfo.InvariantCulture, $"S-section-{id}-{++sections}"), id, kind, label, desc, null, [], null, SettingRowStatus.Wired, false, label);
                }
                else if (string.IsNullOrEmpty(key))
                {
                    row = new SettingRow(string.Create(CultureInfo.InvariantCulture, $"S-keyless-{++keyless:000}"), id, kind, label, desc, null, OptionsOf(o), null, SettingRowStatus.Keyless, false, section);
                }
                else
                {
                    var rowId = rows.ContainsKey("S-" + key) ? "S-" + key + "~" + id : "S-" + key;
                    var status = UnwiredKeys.Contains(key) ? SettingRowStatus.Unwired : SettingRowStatus.Wired;
                    row = new SettingRow(rowId, id, kind, label, desc, key, OptionsOf(o), o["default"]?.DeepClone(), status, AdvancedKeys.Contains(key) || string.Equals(id, "troubleshooting", StringComparison.Ordinal), section, o["invert"] is JsonValue inv && inv.TryGetValue<bool>(out var b) && b);
                }

                list.Add(row);
                rows[row.Id] = row;
            }

            pages.Add(new SettingPage(id, (string)tab["label"]!, (string)tab["group"]!, list));
        }

        return (pages, rows);
    }

    private static SettingRowKind KindOf(string kind) => kind switch
    {
        "toggle" => SettingRowKind.Toggle,
        "select" => SettingRowKind.Select,
        "text" => SettingRowKind.Text,
        "button" => SettingRowKind.Button,
        "section" => SettingRowKind.Section,
        _ => SettingRowKind.Info,
    };

    private static List<SettingOption> OptionsOf(JsonObject o) =>
        [.. (o["options"] as JsonArray ?? []).OfType<JsonArray>().Where(a => a.Count >= 2).Select(a => new SettingOption((string?)a[0] ?? string.Empty, (string?)a[1] ?? string.Empty))];
}
