// Mirrors: Rust cal_editor.rs:1247 availability_for return. Contract from the M4 plan section 4.3; owner cal-edit.

namespace Barbican.Core.Calendar.Editing;

/// <summary>One guest's status for a window: Free (1), Tentative (2) or Busy (3, wins over tentative).</summary>
public sealed record AvailabilityStatus(string Label, int Rank);
