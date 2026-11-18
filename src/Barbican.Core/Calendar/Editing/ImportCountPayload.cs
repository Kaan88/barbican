// Mirrors: Bulwark lib/plugin-hook-catalog.ts:165-:207 (the calendar and task hook payloads). Owner cal-edit; the hook names are in spec/plugins/hooks.csv.

namespace Barbican.Core.Calendar.Editing;

/// <summary>onBeforeEventsImport.</summary>
public sealed record ImportCountPayload(string CalendarId, int Count);
