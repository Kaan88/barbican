// Mirrors: Rust calendar.rs:1250 detail (the context panel beside the grid: when, where, repeats, guests with their answers); Bulwark components/calendar/event-detail-popover.tsx; design BNCalendar "Event details" aside. I-331, B-CAL-054.

namespace Barbican.Presentation.Calendar.Grid;

/// <summary>One guest of the detail panel: <c>Status</c> is "Organizer", "Accepted", "Declined", "Maybe" or "No reply".</summary>
public sealed record EventGuest(string Name, string Initials, string Status);
