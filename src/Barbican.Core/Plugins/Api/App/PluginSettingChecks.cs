// Mirrors: Bulwark lib/plugin-sandbox/assistant/c.settings.ts:60-106 settingRefusal (why a key cannot be changed, to a value when one is given, right now): not in the allow-list (not_allowed when Bulwark or Barbican knows the key, else
// unknown), then the page's availability, then the value (bad_value). Barbican has no administrator lock, hidden flag or restriction on a setting, so locked, hidden and the admin value limits do not occur.
using System.Text.Json.Nodes;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.App;

internal static class PluginSettingChecks
{
    /// <summary>The refusal for <paramref name="name"/> (and for <paramref name="next"/> when it is given), or null; <paramref name="setting"/> is the resolved setting even when the refusal is unavailable, <paramref name="canonical"/> the value to write.</summary>
    internal static PluginSettingRefusal? Check(string name, PluginValue? next, out PluginResolvedSetting? setting, out JsonNode? canonical)
    {
        canonical = null;
        var (resolved, refusal) = PluginSettingsTable.Resolve(name);
        setting = resolved;
        if (refusal is { } why) return why;
        if (next is null || next.Kind == PluginValueKind.Undefined) return null;
        return PluginSettingValues.TryRead(next, out var json) && PluginSettingValues.Allows(resolved!, json, out canonical) ? null : PluginSettingRefusal.BadValue;
    }

    /// <summary>The wire name of a refusal (c.types.ts:143).</summary>
    internal static string Wire(PluginSettingRefusal refusal) => refusal switch
    {
        PluginSettingRefusal.Unknown => "unknown",
        PluginSettingRefusal.NotAllowed => "not_allowed",
        PluginSettingRefusal.BadValue => "bad_value",
        PluginSettingRefusal.Unavailable => "unavailable",
        _ => "stopped",
    };
}
