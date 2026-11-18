// Mirrors: Rust cal_extra.rs:215 task_day, :218 toggle_task_day, :224 task_due_on (the day picked in the mini month while the Tasks view shows: only tasks due that day; picking it again or "clear" lifts the filter); B-CAL-037. Contract from the M4 plan section 4.5; owner cal-views replaces the bodies.

using System.Text.Json.Nodes;
using Barbican.Core.Common;

#pragma warning disable CA1822 // the stubs do not read the instance yet; the real bodies do

namespace Barbican.Presentation.Calendar.Grid;

/// <summary>The mini-month day filter of the Tasks view.</summary>
public sealed class TaskDayFilter
{
    /// <summary>Whether the task is due on <paramref name="day"/> (the list a mini-month pick shows; Rust task_due_on).</summary>
    public static bool IsDueOn(JsonNode task, DateOnly day) => throw new PortPendingException("TaskDayFilter.IsDueOn");

    /// <summary>The picked day, or null.</summary>
    public DateOnly? Day => throw new PortPendingException("TaskDayFilter.Day");

    /// <summary>"yyyy-MM-dd" sets the day (the same day again lifts it); "clear" or an unparsable text lifts it.</summary>
    public void Toggle(string how) => throw new PortPendingException("TaskDayFilter.Toggle");
}
