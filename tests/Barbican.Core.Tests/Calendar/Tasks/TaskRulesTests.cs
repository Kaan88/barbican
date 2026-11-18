// Ported from Rust src/cal_extra.rs tests :984 quick_task_dates, :997 task_labels_and_filters, :1015 task_field_mapping (T-046, T-047, T-048) and src/calendar.rs test :692 sidebar_tasks_capped_and_overdue (T-053). M4 stream cal-ui.

using System.Text.Json.Nodes;
using Barbican.Core.Calendar.Tasks;
using static Barbican.Core.Tests.Calendar.CalendarTestData;

namespace Barbican.Core.Tests.Calendar.Tasks;

public sealed class TaskRulesTests
{
    [Fact]
    [RustTest("cal_extra::quick_task_dates")]
    public void Quick_task_dates()
    {
        var mon = D("2026-11-16");
        Assert.Equal(("Buy milk", D("2026-11-17")), TaskRules.ParseDue("Buy milk tomorrow", mon));
        Assert.Equal(("Report", D("2026-11-20")), TaskRules.ParseDue("Report due fri", mon));
        Assert.Equal(("Call Ann", D("2026-11-23")), TaskRules.ParseDue("Call Ann monday", mon));
        Assert.Equal(("Taxes", D("2026-11-19")), TaskRules.ParseDue("Taxes in 3 days", mon));
        Assert.Equal(("Plan", D("2026-11-23")), TaskRules.ParseDue("Plan next week", mon));
        Assert.Equal(("Ship", D("2026-12-01")), TaskRules.ParseDue("Ship by 2026-12-01", mon));
        Assert.Null(TaskRules.ParseDue("tomorrow", mon));
        Assert.Null(TaskRules.ParseDue("Read a book", mon));
    }

    [Fact]
    [RustTest("cal_extra::task_labels_and_filters")]
    public void Task_labels_and_filters()
    {
        var today = D("2026-11-16");
        static JsonNode T(string due, string progress) => J($$"""{"due": "{{due}}", "progress": "{{progress}}", "showWithoutTime": true}""");
        Assert.Equal(("Today", 1), TaskRules.DueLabel(T("2026-11-16T00:00:00", "needs-action"), today, false));
        Assert.Equal(("Tomorrow", 0), TaskRules.DueLabel(T("2026-11-17T00:00:00", "needs-action"), today, false));
        Assert.Equal(("Overdue · Nov 3", 2), TaskRules.DueLabel(T("2026-11-03T00:00:00", "needs-action"), today, false));
        Assert.Equal(("Nov 3", 0), TaskRules.DueLabel(T("2026-11-03T00:00:00", "completed"), today, false));
        Assert.True(TaskRules.IsOverdue(T("2026-11-15T00:00:00", "in-process"), today));
        Assert.False(TaskRules.IsOverdue(T("2026-11-15T00:00:00", "cancelled"), today));
        Assert.True(TaskRules.Matches(T("2026-11-15T00:00:00", "completed"), 3, today));
        Assert.False(TaskRules.Matches(T("2026-11-15T00:00:00", "completed"), 1, today));
        Assert.Equal(1, TaskRules.PriorityLevel(1));
        Assert.Equal(2, TaskRules.PriorityLevel(5));
        Assert.Equal(3, TaskRules.PriorityLevel(9));
        Assert.Equal(0, TaskRules.PriorityLevel(0));
    }

    [Fact]
    [RustTest("cal_extra::task_field_mapping")]
    public void Task_field_mapping()
    {
        var f = TaskRules.Fields(" Pay ", string.Empty, "2026-11-20", "14:30", 1, 0, 2, 3, "Europe/Istanbul").Value!;
        Assert.Equal("Pay", (string?)f["title"]);
        Assert.Equal("2026-11-20T14:30:00", (string?)f["due"]);
        Assert.Equal("Europe/Istanbul", (string?)f["timeZone"]);
        Assert.Equal(1, (int?)f["priority"]);
        Assert.Equal("weekly", (string?)f["recurrenceRules"]![0]!["frequency"]);
        Assert.Equal("-PT15M", (string?)f["alerts"]!["alert1"]!["trigger"]!["offset"]);
        Assert.Equal("end", (string?)f["alerts"]!["alert1"]!["trigger"]!["relativeTo"]);
        f = TaskRules.Fields("x", string.Empty, string.Empty, null, 0, 2, 0, 3, "UTC").Value!;
        Assert.True(f.ContainsKey("due") && f["due"] is null);
        Assert.Null(f["alerts"]);
        Assert.Equal("completed", (string?)f["progress"]);
        Assert.False(TaskRules.Fields("x", string.Empty, "20-11", null, 0, 0, 0, 0, "UTC").IsOk);
    }

    [Fact]
    [RustTest("calendar::sidebar_tasks_capped_and_overdue")]
    public void Sidebar_tasks_capped_and_overdue()
    {
        var open = Enumerable.Range(0, 9).Select(_ => J("""{"progress": "needs-action"}""")).Append(J("""{"progress": "completed"}""")).ToList();
        Assert.Equal((TaskRules.SidebarTasks, 3), TaskRules.Sidebar(open));
        Assert.Equal((2, 0), TaskRules.Sidebar(open.Take(2).ToList()));
        Assert.True(TaskRules.Overdue("2026-11-09", false, "2026-11-16"));
        Assert.False(TaskRules.Overdue("2026-11-16", false, "2026-11-16"));
        Assert.False(TaskRules.Overdue("2026-11-09", true, "2026-11-16"));
        Assert.False(TaskRules.Overdue(string.Empty, false, "2026-11-16"));
    }
}
