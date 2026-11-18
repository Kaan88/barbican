// Mirrors: Bulwark lib/plugin-hook-catalog.ts:235-236 { key, value, previous } (onBeforeSettingChange, onSettingChange). Owner set-data.

using System.Text.Json.Nodes;

namespace Barbican.Core.Plugins.Payloads;

/// <summary>A preference changes (before) or changed (after): its key, the new value (null: reset to the default) and the value it had.</summary>
public sealed record SettingChangePayload(string Key, JsonNode? Value, JsonNode? Previous);
