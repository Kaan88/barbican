// Mirrors: Rust calendar.rs:355 occurrences, :373 occurrences_in, :598 covers, :746 is_task; B-CAL-004/005 (recurrence expanded on the client, tasks not expanded as events), B-CAL-006 (agenda 30 days).
// Contract from the M4 plan section 4.2; owner cal-data replaces the bodies.

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Calendar;

/// <summary>Events to grid occurrences, on the user's clock.</summary>
public static class CalendarOccurrences
{
    /// <summary>The agenda covers 30 days from its start day (B-CAL-006).</summary>
    public const int AgendaDays = 30;

    /// <summary>The occurrences of the events (not tasks) overlapping [from, to) on the user's clock: ids "id" or "id|recurrenceId", an empty title "(no title)", the event's own zone converted.</summary>
    public static IReadOnlyList<CalendarOccurrence> In(IReadOnlyList<JsonNode> events, DateOnly from, DateOnly to) => throw new PortPendingException("CalendarOccurrences.In");

    /// <summary>Whether the occurrence is on <paramref name="day"/> (an all-day occurrence ends the day before its end).</summary>
    public static bool Covers(CalendarOccurrence occurrence, DateOnly day) => throw new PortPendingException("CalendarOccurrences.Covers");

    /// <summary>Bulwark's isTaskObject: @type Task, or task-only fields on a CalDAV VTODO.</summary>
    public static bool IsTask(JsonNode ev) => throw new PortPendingException("CalendarOccurrences.IsTask");
}
