// Mirrors: src/Barbican.Core/Settings/settings-schema.json (Rust ui/settings-schema.json), M6 plan decision D-M6-A01 (row ids: S-key, S-keyless-NNN in schema order, S-section-page-n). The screen asks the data layer's SettingsCatalog first;
// until set-data lands it, the same schema is read here (D-M6-B04). Owner set-ui.

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Settings;
using Barbican.Core.Settings.Pages;

namespace Barbican.Presentation.Settings.Ui;

/// <summary>The catalog the settings screen draws: the data layer's, or the embedded schema read here.</summary>
public static class SetUiCatalog
{
    /// <summary>The pages in schema order.</summary>
    public static IReadOnlyList<SettingPage> Load()
    {
        try
        {
            var pages = SettingsCatalog.Pages();
            if (pages.Count > 0)
            {
                return pages;
            }
        }
        catch (PortPendingException)
        {
            // set-data has not landed the catalog: read the schema here
        }

        return FromSchema();
    }

    /// <summary>The schema read into pages (public for the tests).</summary>
    public static IReadOnlyList<SettingPage> FromSchema()
    {
        using var stream = SettingsSchema.OpenRaw();
        var root = JsonNode.Parse(stream) as JsonObject ?? [];
        var pages = new List<SettingPage>();
        var keyless = 0;
        foreach (var tab in (root["tabs"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var id = (string?)tab["id"] ?? string.Empty;
            var rows = new List<SettingRow>();
            string? section = null;
            var sections = 0;
            foreach (var r in (tab["rows"] as JsonArray ?? []).OfType<JsonObject>())
            {
                var row = RowOf(r, id, ref keyless, ref sections, section);
                if (row.Kind == SettingRowKind.Section)
                {
                    section = row.Label;
                }

                rows.Add(row);
            }

            pages.Add(new SettingPage(id, (string?)tab["label"] ?? id, (string?)tab["group"] ?? "top", rows));
        }

        return pages;
    }

    private static SettingRow RowOf(JsonObject r, string page, ref int keyless, ref int sections, string? section)
    {
        var kind = ((string?)r["kind"] ?? "info") switch
        {
            "toggle" => SettingRowKind.Toggle,
            "select" => SettingRowKind.Select,
            "text" => SettingRowKind.Text,
            "button" => SettingRowKind.Button,
            "section" => SettingRowKind.Section,
            _ => SettingRowKind.Info,
        };
        var label = (string?)r["label"] ?? string.Empty;
        var desc = (string?)r["desc"] ?? string.Empty;
        var key = (string?)r["key"];
        if (string.IsNullOrEmpty(key))
        {
            key = null;
        }

        if (kind == SettingRowKind.Section)
        {
            sections++;
            return new SettingRow($"S-section-{page}-{sections}", page, kind, label, desc, null, [], null, SettingRowStatus.Keyless, false, label);
        }

        var rowId = key is not null ? "S-" + key : $"S-keyless-{++keyless:000}";
        var options = (r["options"] as JsonArray ?? []).OfType<JsonArray>().Where(a => a.Count >= 2).Select(a => new SettingOption((string?)a[0] ?? string.Empty, (string?)a[1] ?? string.Empty)).ToList();
        return new SettingRow(rowId, page, kind, label, desc, key, options, r["default"]?.DeepClone(), key is null ? SettingRowStatus.Keyless : SettingRowStatus.Wired, false, section);
    }

    /// <summary>Hits for a query over the rows and the page keywords: every word must be somewhere in label, description or keywords; a label match scores 3, a description 2, a keyword 1.</summary>
    public static IReadOnlyList<SettingSearchHit> Search(IReadOnlyList<SettingPage> pages, string query)
    {
        ArgumentNullException.ThrowIfNull(pages);
        try
        {
            var real = SettingSearch.Search(SettingSearch.EntriesOf(pages), query);
            if (real.Count > 0 || string.IsNullOrWhiteSpace(query))
            {
                return real;
            }
        }
        catch (PortPendingException)
        {
            // fall back below
        }

        var words = (query ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
        {
            return [];
        }

        var hits = new List<SettingSearchHit>();
        foreach (var page in pages)
        {
            var nav = SettingsNav.Find(page.Id);
            if (nav is null || nav.Owner == SettingsPageOwner.Later)
            {
                continue;
            }

            var keywords = page.Label + " " + nav.Keywords;
            foreach (var row in page.Rows.Where(r => r.Kind != SettingRowKind.Section))
            {
                if (Score(row, keywords, words) is { } score)
                {
                    hits.Add(new SettingSearchHit(page.Id, row.Id, row.Label, row.Description, score));
                }
            }
        }

        return [.. hits.OrderByDescending(h => h.Score)];
    }

    private static double? Score(SettingRow row, string keywords, string[] words)
    {
        double score = 0;
        foreach (var w in words)
        {
            if (row.Label.Contains(w, StringComparison.OrdinalIgnoreCase))
            {
                score += 3;
            }
            else if (row.Description.Contains(w, StringComparison.OrdinalIgnoreCase))
            {
                score += 2;
            }
            else if (keywords.Contains(w, StringComparison.OrdinalIgnoreCase))
            {
                score += 1;
            }
            else
            {
                return null;
            }
        }

        return score;
    }
}
