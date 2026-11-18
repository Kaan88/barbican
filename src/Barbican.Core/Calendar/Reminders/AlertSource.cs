// Mirrors: Rust reminders.rs:47 EdRow.source. Contract from the M4 plan section 4.1; owner cal-data.

namespace Barbican.Core.Calendar.Reminders;

/// <summary>The alert a row came from: its id and the row as read.</summary>
public sealed record AlertSource(string Id, ReminderRow Row);
