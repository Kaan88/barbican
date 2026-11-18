// Mirrors: Bulwark lib/plugin-hook-catalog.ts:165-:207 (the calendar and task hook payloads). Owner cal-edit; the hook names are in spec/plugins/hooks.csv.

using System.Text.Json.Nodes;

namespace Barbican.Core.Calendar.Editing;

/// <summary>onCheckEventConflicts: the event being edited and the warnings so far; a handler adds its own.</summary>
public sealed record ConflictCheckPayload(JsonNode Event, IReadOnlyList<string> Warnings);
