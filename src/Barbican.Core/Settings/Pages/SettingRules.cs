// Mirrors: Bulwark stores/settings-store.ts (typed setters), components/settings/settings-section.tsx (SettingItem kinds); B-SET-001..005. Contract from the M6 plan section 4.2; owner set-data.

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Settings.Pages;

/// <summary>The pure rules a written value follows before it reaches prefs.json.</summary>
public static class SettingRules
{
    /// <summary>Checks a value for a row: a toggle takes a boolean (the strings "true" and "false" are coerced), a select one of its option values, a text a trimmed string; null resets to the default. Info, button and section rows refuse any value.</summary>
    public static SettingCheck Coerce(SettingRow row, JsonNode? value) => throw new PortPendingException("SettingRules.Coerce");
}
