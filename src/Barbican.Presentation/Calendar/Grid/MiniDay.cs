// Mirrors: Rust calendar.rs:508 MiniDay (day, in_month, today, selected, date, busy); Bulwark components/calendar/mini-calendar.tsx. I-302, I-303.

namespace Barbican.Presentation.Calendar.Grid;

/// <summary>One day of the mini month; <c>Busy</c> when a visible event covers it, <c>Selected</c> when it is in the shown week or day (or the picked task day).</summary>
public sealed record MiniDay(DateOnly Day, bool InMonth, bool Today, bool Selected, bool Busy);
