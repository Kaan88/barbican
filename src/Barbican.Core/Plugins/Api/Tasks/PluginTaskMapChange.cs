// Mirrors: Bulwark lib/plugin-sandbox/host-api-calendar.ts:604-609 MapChange: null for the whole map (removes every entry), else the entries named, each a value to set or null to remove.
using System.Text.Json.Nodes;

namespace Barbican.Core.Plugins.Api.Tasks;

/// <summary>A change a plugin asked for to one of a task's maps (alerts, links, relatedTo, keywords); entries not named are left as they are.</summary>
internal sealed record PluginTaskMapChange(bool ClearAll, IReadOnlyList<(string Key, JsonNode? Value)> Entries);
