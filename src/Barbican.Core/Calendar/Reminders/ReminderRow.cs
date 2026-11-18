// Mirrors: Rust reminders.rs:36 Row; Bulwark components/calendar/alert-list-editor.tsx. Contract from the M4 plan section 4.1; owner cal-data.

namespace Barbican.Core.Calendar.Reminders;

/// <summary>One editable reminder: an amount and unit before the start and how it fires.</summary>
/// <param name="Value">The amount.</param>
/// <param name="Unit">0 at time of event, 1 minutes, 2 hours, 3 days, 4 weeks.</param>
/// <param name="Action">0 display (notification), 1 email.</param>
public sealed record ReminderRow(long Value, int Unit, int Action);
