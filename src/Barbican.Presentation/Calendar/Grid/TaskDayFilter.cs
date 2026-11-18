// Mirrors: Rust cal_extra.rs:215 task_day, :218 toggle_task_day, :224 task_due_on (the day picked in the mini month while the Tasks view shows: only tasks due that day; picking it again or "clear" lifts the filter); B-CAL-037.

using System.Globalization;
using System.Text.Json.Nodes;

namespace Barbican.Presentation.Calendar.Grid;

/// <summary>The mini-month day filter of the Tasks view.</summary>
public sealed class TaskDayFilter
{
    /// <summary>The picked day, or null.</summary>
    public DateOnly? Day { get; private set; }

    /// <summary>Whether the task is due on <paramref name="day"/> (the list a mini-month pick shows; Rust task_due_on). A due date that does not parse is due on no day.</summary>
    public static bool IsDueOn(JsonNode task, DateOnly day)
    {
        ArgumentNullException.ThrowIfNull(task);
        return task["due"] is JsonValue v && v.TryGetValue<string>(out var due) && DueDate(due) is { } d && d == day;
    }

    /// <summary>The date part of a JSCalendar local date-time ("2026-11-18T09:00:00", "2026-11-18").</summary>
    public static DateOnly? DueDate(string? due)
    {
        if (string.IsNullOrEmpty(due) || due.Length < 10)
        {
            return null;
        }

        return DateOnly.TryParseExact(due.AsSpan(0, 10), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    /// <summary>"yyyy-MM-dd" sets the day (the same day again lifts it); "clear" or an unparsable text lifts it.</summary>
    public void Toggle(string how)
    {
        ArgumentNullException.ThrowIfNull(how);
        DateOnly? picked = DateOnly.TryParseExact(how, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
        Day = picked is null || Day == picked ? null : picked;
    }
}
