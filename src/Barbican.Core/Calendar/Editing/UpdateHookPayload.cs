// Mirrors: Bulwark lib/plugin-hook-catalog.ts:165-:207 (the calendar and task hook payloads). Owner cal-edit; the hook names are in spec/plugins/hooks.csv.

using System.Text.Json.Nodes;

namespace Barbican.Core.Calendar.Editing;

/// <summary>onBeforeEventUpdate and onBeforeTaskUpdate: the id and the patch about to be written.</summary>
public sealed record UpdateHookPayload(string Id, JsonNode Patch);
