// Mirrors: Rust cal_extra.rs:183 priority_level, :186 is_overdue, :192 due_label, :205 task_matches, :268 parse_task_due, :312 TASK_ALERT_MIN, :361 task_fields, calendar.rs:752 SIDEBAR_TASKS, :755 sidebar_tasks, :762 task_overdue;
// Bulwark components/calendar/task-modal.tsx, task-list-view.tsx, lib/calendar-tasks.ts; B-CAL-007, 036, 038, 039. JSCalendar Task (RFC 8984 2.2): due, timeZone, priority, progress (needs-action, in-process, completed, cancelled), recurrenceRules, alerts relative to the due date.
// Contract from the M4 plan section 4.4; owner cal-ui.

using System.Globalization;
using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;

namespace Barbican.Core.Calendar.Tasks;

/// <summary>The pure task rules: due labels and filters, quick-add dates, the fields of a task save, the sidebar list.</summary>
public static class TaskRules
{
    /// <summary>The sidebar lists this many open tasks, then "Show all" (B-CAL-007).</summary>
    public const int SidebarTasks = 6;

    /// <summary>Task alert choices in minutes before the due time: none (-1), at due (0), 5, 15, 30, 60, 1440 (B-CAL-036).</summary>
    public static IReadOnlyList<int> AlertMinutes { get; } = [-1, 0, 5, 15, 30, 60, 1440];

    private static readonly string[] Frequencies = ["daily", "weekly", "monthly", "yearly"];
    private static readonly string[] Progresses = ["needs-action", "in-process", "completed", "cancelled"];
    private static readonly int[] Priorities = [0, 1, 5, 9];

    /// <summary>JMAP priority 1 to 4 is high (1), 5 medium (2), 6 to 9 low (3), 0 none (0).</summary>
    public static int PriorityLevel(long priority) => priority switch
    {
        >= 1 and <= 4 => 1,
        5 => 2,
        >= 6 and <= 9 => 3,
        _ => 0,
    };

    /// <summary>Before <paramref name="today"/> and not completed or cancelled.</summary>
    public static bool IsOverdue(JsonNode task, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(task);
        return !Finished(task) && ParseLocal((string?)task["due"]) is { } due && DateOnly.FromDateTime(due) < today;
    }

    /// <summary>"Today" (tone 1), "Tomorrow" (0), "Overdue · Nov 3" (2), a plain date "Nov 3" or "Nov 3, 2027" (0; a completed task has no overdue label); the time is added for a timed due date. Tone is the sort rank: 0 dim, 1 today, 2 overdue.</summary>
    public static (string Label, int Rank) DueLabel(JsonNode task, DateOnly today, bool twelveHour)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (ParseLocal((string?)task["due"]) is not { } due)
        {
            return (string.Empty, 0);
        }

        var withoutTime = task["showWithoutTime"] is JsonValue v && v.TryGetValue<bool>(out var b) && b;
        var time = withoutTime || due.TimeOfDay == TimeSpan.Zero ? string.Empty : " " + FormatTime(due, twelveHour);
        var date = due.Year == today.Year
            ? due.ToString("MMM d", CultureInfo.InvariantCulture)
            : due.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
        var day = DateOnly.FromDateTime(due);
        if (Finished(task))
        {
            return (date + time, 0);
        }

        if (day == today)
        {
            return ("Today" + time, 1);
        }

        if (day == today.AddDays(1))
        {
            return ("Tomorrow" + time, 0);
        }

        return day < today ? ("Overdue · " + date, 2) : (date + time, 0);
    }

    /// <summary>Which tasks a Tasks-view filter lists: 0 all, 1 pending (not completed or cancelled), 2 overdue, 3 completed.</summary>
    public static bool Matches(JsonNode task, int filter, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(task);
        return filter switch
        {
            1 => !Finished(task),
            2 => IsOverdue(task, today),
            3 => string.Equals((string?)task["progress"], "completed", StringComparison.Ordinal),
            _ => true,
        };
    }

    /// <summary>Quick add: "Buy milk tomorrow", "Report due fri", "Call Ann monday", "Taxes in 3 days", "Plan next week", "Ship by 2026-12-01" give the title without the date words and the date; a text that is only a date word, or has none, gives null.</summary>
    public static (string Title, DateOnly Due)? ParseDue(string text, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(text);
        var all = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (all.Length < 2)
        {
            return null;
        }

        var words = all.Select(w => w.ToLowerInvariant()).ToArray();
        var last = words[^1];

        (string, DateOnly)? Hit(int dropped, DateOnly date) => (KeepTitle(all, dropped), date);

        if (words.Length >= 3 && Is(words[^3], "in") && (Is(last, "days") || Is(last, "day")))
        {
            return int.TryParse(words[^2], NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? Hit(3, today.AddDays(n)) : null;
        }

        if (words.Length >= 3 && Is(words[^2], "next") && Is(last, "week"))
        {
            return Hit(2, today.AddDays(7));
        }

        if (DateOnly.TryParseExact(last, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var iso))
        {
            return Hit(1, iso);
        }

        switch (last)
        {
            case "today" or "tonight":
                return Hit(1, today);
            case "tomorrow":
                return Hit(1, today.AddDays(1));
        }

        string[] shortNames = ["mon", "tue", "wed", "thu", "fri", "sat", "sun"];
        string[] full = ["monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday"];
        for (var i = 0; i < 7; i++)
        {
            if (Is(last, shortNames[i]) || Is(last, shortNames[i] + ".") || Is(last, full[i]))
            {
                var todayIndex = ((int)today.DayOfWeek + 6) % 7;
                var ahead = (((i - todayIndex) % 7) + 7) % 7;
                return Hit(1, today.AddDays(ahead == 0 ? 7 : ahead));
            }
        }

        return null;
    }

    /// <summary>The fields of a task (create or edit): title trimmed, due "yyyy-MM-ddTHH:mm:ss" with timeZone (null clears), priority, progress (0 needs-action, 1 in-process, 2 completed), repeat (0 none, 1 daily, 2 weekly, 3 monthly, 4 yearly), alert (an index into
    /// <see cref="AlertMinutes"/>; offset relative to the end), notes; an invalid date or time is a validation error.</summary>
    public static Result<JsonObject> Fields(string title, string notes, string dueDate, string? dueTime, int priority, int progress, int repeat, int alert, string timeZone)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(dueDate);
        var m = new JsonObject
        {
            ["title"] = title.Trim(),
            ["description"] = notes.Trim(),
        };
        var due = dueDate.Trim();
        if (due.Length == 0)
        {
            m["due"] = null;
        }
        else
        {
            if (!DateOnly.TryParseExact(due, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            {
                return Result<JsonObject>.Fail(new ValidationError("Use YYYY-MM-DD for the due date."));
            }

            if (dueTime is not null)
            {
                if (!TimeOnly.TryParseExact(dueTime.Trim(), ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
                {
                    return Result<JsonObject>.Fail(new ValidationError("Use HH:MM for the time."));
                }

                m["due"] = d.ToDateTime(t).ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
                m["timeZone"] = timeZone;
                m["showWithoutTime"] = false;
            }
            else
            {
                m["due"] = d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T00:00:00";
                m["timeZone"] = null;
                m["showWithoutTime"] = true;
            }
        }

        m["priority"] = Priorities[Math.Clamp(priority, 0, 3)];
        m["progress"] = Progresses[Math.Clamp(progress, 0, 3)];
        m["recurrenceRules"] = repeat is >= 1 and <= 4
            ? new JsonArray(new JsonObject { ["@type"] = "RecurrenceRule", ["frequency"] = Frequencies[repeat - 1] })
            : null;
        m["alerts"] = AlertOf(AlertMinutes[Math.Clamp(alert, 0, 6)], due.Length > 0);
        return Result<JsonObject>.Ok(m);
    }

    private static JsonObject? AlertOf(int mins, bool hasDue) => mins < 0 || !hasDue
        ? null
        : new JsonObject
        {
            ["alert1"] = new JsonObject
            {
                ["@type"] = "Alert",
                ["trigger"] = new JsonObject
                {
                    ["@type"] = "OffsetTrigger",
                    ["offset"] = mins == 0 ? "PT0S" : $"-PT{mins}M",
                    ["relativeTo"] = "end",
                },
                ["action"] = "display",
            },
        };

    /// <summary>(rows to show, open tasks left out) for tasks sorted open-first.</summary>
    public static (int Shown, int Hidden) Sidebar(IReadOnlyList<JsonNode> sortedTasks)
    {
        ArgumentNullException.ThrowIfNull(sortedTasks);
        var open = sortedTasks.Count(t => !string.Equals((string?)t["progress"], "completed", StringComparison.Ordinal));
        var shown = Math.Min(open, SidebarTasks);
        return (shown, open - shown);
    }

    /// <summary>A due date string "yyyy-MM-dd..." earlier than today's "yyyy-MM-dd", for an open task.</summary>
    public static bool Overdue(string due, bool done, string today)
    {
        ArgumentNullException.ThrowIfNull(due);
        ArgumentNullException.ThrowIfNull(today);
        return !done && due.Length > 0 && string.CompareOrdinal(due, today) < 0;
    }

    /// <summary>Completed or cancelled.</summary>
    public static bool Finished(JsonNode task)
    {
        ArgumentNullException.ThrowIfNull(task);
        var p = (string?)task["progress"];
        return p is "completed" or "cancelled";
    }

    private static bool Is(string a, string b) => string.Equals(a, b, StringComparison.Ordinal);

    private static string KeepTitle(string[] all, int dropped)
    {
        var s = string.Join(' ', all.Take(all.Length - dropped));
        foreach (var w in new[] { " by", " due", " on" })
        {
            if (s.EndsWith(w, StringComparison.OrdinalIgnoreCase) && s.Length > w.Length)
            {
                s = s[..^w.Length];
            }
        }

        return s.Trim();
    }

    private static DateTime? ParseLocal(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var head = text.Length >= 19 ? text[..19] : text;
        if (DateTime.TryParseExact(head, "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            return dt;
        }

        return DateTime.TryParseExact(text.Length >= 10 ? text[..10] : text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    private static string FormatTime(DateTime t, bool twelveHour) =>
        twelveHour
            ? t.ToString("h:mm tt", CultureInfo.InvariantCulture).ToLowerInvariant()
            : t.ToString("HH:mm", CultureInfo.InvariantCulture);
}
