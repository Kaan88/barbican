// Mirrors: Rust cal_editor.rs:1300 find_free_slots return. Contract from the M4 plan section 4.3; owner cal-edit.

namespace Barbican.Core.Calendar.Editing;

/// <summary>A suggested meeting time on the user's clock.</summary>
public sealed record FreeSlot(DateTime Start, DateTime End);
