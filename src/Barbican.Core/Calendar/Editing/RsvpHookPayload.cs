// Mirrors: Bulwark lib/plugin-hook-catalog.ts:165-:207 (the calendar and task hook payloads). Owner cal-edit; the hook names are in spec/plugins/hooks.csv.

namespace Barbican.Core.Calendar.Editing;

/// <summary>onBeforeEventRsvp and onEventRsvp.</summary>
public sealed record RsvpHookPayload(string EventId, string Status);
