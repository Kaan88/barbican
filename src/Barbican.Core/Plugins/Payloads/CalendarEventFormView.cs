// Mirrors: Bulwark lib/plugin-types.ts CalendarEventFormView (onCalendarEventFormOpen observer, onCalendarEventFormSave second argument). Owner cal-ui.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>The event form as plugins see it: the fields, the uid of the event being edited (null for a new one) and the guests' addresses.</summary>
public sealed record CalendarEventFormView(string Title, string Description, string Start, string End, bool IsAllDay, string Location, string VirtualLocation, string CalendarId, string? Uid, IReadOnlyList<string> Attendees);
