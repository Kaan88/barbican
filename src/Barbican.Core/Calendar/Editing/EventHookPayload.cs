// Mirrors: Bulwark lib/plugin-hook-catalog.ts:165-:207 (the calendar and task hook payloads). Owner cal-edit; the hook names are in spec/plugins/hooks.csv.

using System.Text.Json.Nodes;

namespace Barbican.Core.Calendar.Editing;

/// <summary>An event (or task) as the hooks see it: its id (null before it exists) and the JSCalendar object.</summary>
public sealed record EventHookPayload(string? EventId, JsonNode Event);
