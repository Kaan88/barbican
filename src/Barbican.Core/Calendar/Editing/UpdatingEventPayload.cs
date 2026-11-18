// Mirrors: Bulwark lib/plugin-hook-catalog.ts:165-:207 (the calendar and task hook payloads). Owner cal-edit; the hook names are in spec/plugins/hooks.csv.

using System.Text.Json.Nodes;

namespace Barbican.Core.Calendar.Editing;

/// <summary>onUpdatingEvent: the patch for the store and the patch for the server; a handler may change the server's.</summary>
public sealed record UpdatingEventPayload(JsonNode LocalUpdates, JsonNode ServerUpdates, string EventId);
