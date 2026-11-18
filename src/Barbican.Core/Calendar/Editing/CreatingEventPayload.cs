// Mirrors: Bulwark lib/plugin-hook-catalog.ts:165-:207 (the calendar and task hook payloads). Owner cal-edit; the hook names are in spec/plugins/hooks.csv.

using System.Text.Json.Nodes;

namespace Barbican.Core.Calendar.Editing;

/// <summary>onCreatingEvent: the event as the store keeps it and as the server gets it; a handler may change the server's.</summary>
public sealed record CreatingEventPayload(JsonNode LocalEvent, JsonNode ServerEvent);
