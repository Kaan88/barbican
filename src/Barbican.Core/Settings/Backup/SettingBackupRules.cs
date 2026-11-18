// Mirrors: Rust settings.rs:167 import (flat or wrapped); Bulwark settings export (components/settings/about-data-settings.tsx; a flat settings object or one wrapped in `settings`); B-SET-006. Contract from the M6 plan section 4.3; owner set-data.

using System.Text.Json.Nodes;
using Barbican.Core.Settings.Pages;

namespace Barbican.Core.Settings.Backup;

/// <summary>The pure part of import.</summary>
public static class SettingBackupRules
{
    /// <summary>Classifies an export document: a flat Bulwark object, a Bulwark object wrapped in <c>settings</c>, or a Barbican file (<c>app</c> = barbican with <c>settings</c> and <c>desktop</c>); null for anything that is not a JSON object.</summary>
    public static SettingBackupPlan? Plan(JsonNode document, IReadOnlyList<SettingPage> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);
        if (document is not JsonObject root)
        {
            return null;
        }

        var own = root["app"] is JsonValue app && app.TryGetValue<string>(out var name) && string.Equals(name, "barbican", StringComparison.Ordinal);
        var settings = own || root["settings"] is JsonObject ? root["settings"] as JsonObject ?? [] : root;
        var known = pages.SelectMany(p => p.Rows).Select(r => r.Key).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var keys = settings.Select(kv => kv.Key).ToList();
        return new SettingBackupPlan(!own, own && root["desktop"] is JsonObject, keys.Count(known.Contains), [.. keys.Where(k => !known.Contains(k))]);
    }
}
