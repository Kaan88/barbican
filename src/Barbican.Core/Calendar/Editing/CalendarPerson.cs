// Mirrors: Rust cal_editor.rs:22 Person. Contract from the M4 plan section 4.3; owner cal-edit.

namespace Barbican.Core.Calendar.Editing;

/// <summary>A guest or an organiser: a display name (may be empty) and an address.</summary>
public sealed record CalendarPerson(string Name, string Email);
