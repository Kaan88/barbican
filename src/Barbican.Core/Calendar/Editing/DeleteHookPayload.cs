// Mirrors: Bulwark lib/plugin-hook-catalog.ts:165-:207 (the calendar and task hook payloads). Owner cal-edit; the hook names are in spec/plugins/hooks.csv.

namespace Barbican.Core.Calendar.Editing;

/// <summary>onBeforeEventDelete, onAfterEventDelete, onBeforeTaskDelete, onTaskDelete.</summary>
public sealed record DeleteHookPayload(string Id, string Title);
