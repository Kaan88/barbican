// Mirrors: Rust reminders.rs:47 EdRow. Contract from the M4 plan section 4.1; owner cal-data.

namespace Barbican.Core.Calendar.Reminders;

/// <summary>An editor row and the event alert it was read from (id and row as read): a row left as it was keeps that alert under its id.</summary>
public sealed record AlertEditRow(ReminderRow Row, AlertSource? Source = null);
