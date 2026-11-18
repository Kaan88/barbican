// Mirrors: Rust cal_extra.rs:183 priority_level, :186 is_overdue, :192 due_label, :205 task_matches, :268 parse_task_due, :312 TASK_ALERT_MIN, :361 task_fields, calendar.rs:752 SIDEBAR_TASKS, :755 sidebar_tasks, :762 task_overdue;
// Bulwark components/calendar/task-modal.tsx, task-list-view.tsx, lib/calendar-tasks.ts; B-CAL-007, 036, 038, 039. JSCalendar Task (RFC 8984 2.2): due, timeZone, priority, progress (needs-action, in-process, completed, cancelled), recurrenceRules, alerts relative to the due date.
// Contract from the M4 plan section 4.4; owner cal-ui replaces the bodies.

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Calendar.Tasks;

/// <summary>The pure task rules: due labels and filters, quick-add dates, the fields of a task save, the sidebar list.</summary>
public static class TaskRules
{
    /// <summary>The sidebar lists this many open tasks, then "Show all" (B-CAL-007).</summary>
    public const int SidebarTasks = 6;

    /// <summary>Task alert choices in minutes before the due time: none (-1), at due (0), 5, 15, 30, 60, 1440 (B-CAL-036).</summary>
    public static IReadOnlyList<int> AlertMinutes { get; } = [-1, 0, 5, 15, 30, 60, 1440];

    /// <summary>JMAP priority 1 to 4 is high (1), 5 medium (2), 6 to 9 low (3), 0 none (0).</summary>
    public static int PriorityLevel(long priority) => throw new PortPendingException("TaskRules.PriorityLevel");

    /// <summary>Before <paramref name="today"/> and not completed or cancelled.</summary>
    public static bool IsOverdue(JsonNode task, DateOnly today) => throw new PortPendingException("TaskRules.IsOverdue");

    /// <summary>"Today" (tone 1), "Tomorrow" (0), "Overdue · Nov 3" (2), a plain date "Nov 3" or "Nov 3, 2027" (0; a completed task has no overdue label); the time is added for a timed due date. Tone is the sort rank: 0 dim, 1 today, 2 overdue.</summary>
    public static (string Label, int Rank) DueLabel(JsonNode task, DateOnly today, bool twelveHour) => throw new PortPendingException("TaskRules.DueLabel");

    /// <summary>Which tasks a Tasks-view filter lists: 0 all, 1 pending (not completed or cancelled), 2 overdue, 3 completed.</summary>
    public static bool Matches(JsonNode task, int filter, DateOnly today) => throw new PortPendingException("TaskRules.Matches");

    /// <summary>Quick add: "Buy milk tomorrow", "Report due fri", "Call Ann monday", "Taxes in 3 days", "Plan next week", "Ship by 2026-12-01" give the title without the date words and the date; a text that is only a date word, or has none, gives null.</summary>
    public static (string Title, DateOnly Due)? ParseDue(string text, DateOnly today) => throw new PortPendingException("TaskRules.ParseDue");

    /// <summary>The fields of a task (create or edit): title trimmed, due "yyyy-MM-ddTHH:mm:ss" with timeZone (null clears), priority, progress (0 needs-action, 1 in-process, 2 completed), repeat (0 none, 1 daily, 2 weekly, 3 monthly, 4 yearly), alert (an index into
    /// <see cref="AlertMinutes"/>; offset relative to the end), notes; an invalid date or time is a validation error.</summary>
    public static Result<JsonObject> Fields(string title, string notes, string dueDate, string? dueTime, int priority, int progress, int repeat, int alert, string timeZone) => throw new PortPendingException("TaskRules.Fields");

    /// <summary>(rows to show, open tasks left out) for tasks sorted open-first.</summary>
    public static (int Shown, int Hidden) Sidebar(IReadOnlyList<JsonNode> sortedTasks) => throw new PortPendingException("TaskRules.Sidebar");

    /// <summary>A due date string "yyyy-MM-dd..." earlier than today's "yyyy-MM-dd", for an open task.</summary>
    public static bool Overdue(string due, bool done, string today) => throw new PortPendingException("TaskRules.Overdue");
}
