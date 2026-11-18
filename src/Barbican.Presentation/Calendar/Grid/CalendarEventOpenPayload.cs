// Mirrors: Bulwark lib/plugin-hook-catalog.ts:162 onCalendarEventOpen (CalendarEventView), :180 onCalendarDateChange ({ date }), :181 onCalendarViewChange ({ view }), :182 onCalendarChange ({ calendarId, change }), :183 onCalendarVisibilityToggle ({ calendarId, visible }).
// The payloads of the five hooks cal-views fires (spec/plugins/hooks.csv, owner cal-views).

namespace Barbican.Presentation.Calendar.Grid;

/// <summary>onCalendarEventOpen: the event the user opened (an occurrence of a series carries its recurrence id).</summary>
public sealed record CalendarEventOpenPayload(string Id, string? RecurrenceId, string Title, DateTime Start, DateTime End, bool AllDay, string CalendarId, string Location);
