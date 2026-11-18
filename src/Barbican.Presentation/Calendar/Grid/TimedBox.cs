// Mirrors: Rust calendar.rs:526-:566 (timed events with overlap lanes, all-day heads), :18 HOUR_PX; Bulwark components/calendar/calendar-week-view.tsx (overlap groups, the all-day strip rows). B-CAL-009; G-003.

using Barbican.Core.Calendar;

namespace Barbican.Presentation.Calendar.Grid;

/// <summary>A timed event placed in a day column: <c>Top</c> and <c>Height</c> in px from the first visible hour, <c>Lane</c> of <c>Lanes</c> side by side.</summary>
public sealed record TimedBox(CalendarOccurrence Occurrence, int Column, double Top, double Height, int Lane, int Lanes);
