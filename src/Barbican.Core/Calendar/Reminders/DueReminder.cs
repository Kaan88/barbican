// Mirrors: Rust reminders.rs:191 Due. Contract from the M4 plan section 4.1; owner cal-data.

namespace Barbican.Core.Calendar.Reminders;

/// <summary>A reminder that comes due. <c>Event</c> is the event id, or "id|recurrenceId" for an occurrence; <c>Key</c> is <c>event:alert:fireMs</c>.</summary>
public sealed record DueReminder(string Key, string Event, string Alert, long FireMs, long? StartMs, string Title, string Calendar, bool Task);
