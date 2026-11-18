// Mirrors: Rust calendar.rs:38 Occ. Contract from the M4 plan section 4.2; owner cal-data.

namespace Barbican.Core.Calendar;

/// <summary>
/// One thing on the grid: an event occurrence, a task or a birthday, on the user's wall clock. <c>Id</c> is what the views hand back: the event id, "id|recurrenceId" for an occurrence of a series, "task:ID" or "bday:KEY"
/// (birthday keys are "bday:card:entry:year"); <c>Base</c> is the event it comes from; <c>RecurrenceId</c> is null for a single event.
/// </summary>
public sealed record CalendarOccurrence(string Id, string Base, string? RecurrenceId, string Title, DateTime Start, DateTime End, bool AllDay, string Calendar, string Location, CalendarOccurrenceKind Kind, bool Done);
