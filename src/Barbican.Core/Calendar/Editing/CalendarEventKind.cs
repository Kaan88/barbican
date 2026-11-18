// Reads of the kind and the progress of a stored event or task (JSCalendar @type and progress); used by the writers.

using System.Text.Json.Nodes;

namespace Barbican.Core.Calendar.Editing;

/// <summary>Reads of the kind of a stored event.</summary>
internal static class CalendarEventKind
{
    public static bool IsTask(this JsonNode? ev) => string.Equals(ev.Str("@type"), "Task", StringComparison.Ordinal);

    public static bool IsDone(this JsonNode? ev) => string.Equals(ev.Str("progress"), "completed", StringComparison.Ordinal);
}
