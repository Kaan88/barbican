// Mirrors: Rust calendar.rs:53 Occ.kind (0 event, 1 task, 2 birthday). Contract from the M4 plan section 4.2; owner cal-data.

namespace Barbican.Core.Calendar;

/// <summary>What a calendar occurrence is.</summary>
public enum CalendarOccurrenceKind
{
    Event,
    Task,
    Birthday,
}
