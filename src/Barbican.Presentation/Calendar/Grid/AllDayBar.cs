// Mirrors: Rust calendar.rs:526-:566 (timed events with overlap lanes, all-day heads), :18 HOUR_PX; Bulwark components/calendar/calendar-week-view.tsx (overlap groups, the all-day strip rows). B-CAL-009; G-003.

using Barbican.Core.Calendar;

namespace Barbican.Presentation.Calendar.Grid;

/// <summary>An all-day (or multi-day) event in the strip: from <c>Column</c> for <c>Span</c> columns, on <c>Row</c>.</summary>
public sealed record AllDayBar(CalendarOccurrence Occurrence, int Column, int Span, int Row);
