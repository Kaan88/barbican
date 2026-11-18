// Mirrors: Bulwark lib/plugin-hook-catalog.ts:162 onCalendarEventOpen (CalendarEventView), :180 onCalendarDateChange ({ date }), :181 onCalendarViewChange ({ view }), :182 onCalendarChange ({ calendarId, change }), :183 onCalendarVisibilityToggle ({ calendarId, visible }).
// The payloads of the five hooks cal-views fires (spec/plugins/hooks.csv, owner cal-views).

namespace Barbican.Presentation.Calendar.Grid;

/// <summary>onCalendarVisibilityToggle: a calendar was shown or hidden.</summary>
public sealed record CalendarVisibilityPayload(string CalendarId, bool Visible);
