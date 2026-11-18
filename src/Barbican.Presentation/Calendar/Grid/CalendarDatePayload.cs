// Mirrors: Bulwark lib/plugin-hook-catalog.ts:162 onCalendarEventOpen (CalendarEventView), :180 onCalendarDateChange ({ date }), :181 onCalendarViewChange ({ view }), :182 onCalendarChange ({ calendarId, change }), :183 onCalendarVisibilityToggle ({ calendarId, visible }).
// The payloads of the five hooks cal-views fires (spec/plugins/hooks.csv, owner cal-views).

namespace Barbican.Presentation.Calendar.Grid;

/// <summary>onCalendarDateChange: the date now shown ("yyyy-MM-dd").</summary>
public sealed record CalendarDatePayload(string Date);
