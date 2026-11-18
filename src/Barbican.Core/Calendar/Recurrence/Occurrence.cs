// Mirrors: Rust recurrence.rs:369 Occurrence. Contract from the M4 plan section 4.1; owner cal-data.

using System.Text.Json.Nodes;

namespace Barbican.Core.Calendar.Recurrence;

/// <summary>One occurrence of an event: start and end on the event's own wall clock, its recurrence id (null for a single event) and the override that changes it, if any.</summary>
public sealed record Occurrence(DateTime Start, DateTime End, string? RecurrenceId, JsonNode? Patch);
