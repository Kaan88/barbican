// Mirrors: Rust calendar.rs:1250 detail (the context panel beside the grid: when, where, repeats, guests with their answers); Bulwark components/calendar/event-detail-popover.tsx; design BNCalendar "Event details" aside. I-331, B-CAL-054.

using Barbican.Core.Calendar;

namespace Barbican.Presentation.Calendar.Grid;

/// <summary>What the detail panel and the hover card show for one occurrence.</summary>
public sealed record EventDetail(string Id, string Title, string When, string Location, string Description, string CalendarName, string CalendarColor, bool Repeats, bool Done, CalendarOccurrenceKind Kind, IReadOnlyList<EventGuest> Guests);
