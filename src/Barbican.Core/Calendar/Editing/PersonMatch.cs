// Mirrors: Rust cal_extra.rs:53 people_matching (a contact or a group with its members). Contract from the M4 plan section 4.3; owner cal-edit.

namespace Barbican.Core.Calendar.Editing;

/// <summary>A hit of the guest search: a person, or a group with the members to add.</summary>
public sealed record PersonMatch(CalendarPerson Person, IReadOnlyList<CalendarPerson> Members);
