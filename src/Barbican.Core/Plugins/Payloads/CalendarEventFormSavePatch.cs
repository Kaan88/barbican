// Mirrors: Bulwark lib/plugin-hooks.ts CalendarEventFormSavePatch (onCalendarEventFormSave transform): a handler may set the event's virtual location (a meeting link) in the same save that sends the scheduling messages. Owner cal-ui.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>What onCalendarEventFormSave handlers may set on the event being saved.</summary>
public sealed record CalendarEventFormSavePatch(string? VirtualLocation = null);
