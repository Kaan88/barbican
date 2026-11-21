// Mirrors: Bulwark lib/plugin-sandbox/host-api-calendar.ts:891-893 isDone and :939-949 loadTask / requireTask (the task by its id in the store the calendar shows).
using System.Text.Json.Nodes;
using Barbican.Core.Calendar;

namespace Barbican.Core.Plugins.Api.Tasks;

internal static class PluginTaskData
{
    /// <summary>The task with exactly this id (a task has no occurrence ids here), or null.</summary>
    internal static JsonNode? Find(CalendarSnapshot snapshot, string id) =>
        snapshot.Events.FirstOrDefault(e => string.Equals(e.Str("id"), id, StringComparison.Ordinal) && CalendarOccurrences.IsTask(e));

    /// <summary>isDone: completed or cancelled.</summary>
    internal static bool IsDone(JsonNode task) => task.Str("progress") is "completed" or "cancelled";
}
