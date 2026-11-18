// M4 stream cal-ui acceptance: Tasks view filters with counts, sort, selection versus open, the day filter, quick add words, task menu actions, the capped sidebar list, and speed with thousands of tasks (PARITY-ROUND2).

using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using Barbican.Presentation.Calendar.Tasks;
using Barbican.Presentation.Tests.Calendar.Editors;

namespace Barbican.Presentation.Tests.Calendar.Tasks;

public sealed class TasksViewModelTests
{
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private readonly FakeSource _source = new();
    private readonly FakeWriter _writer = new();
    private readonly RecordingEditors _editors = new();
    private readonly TimeProvider _time = new Clock(new DateTimeOffset(2026, 11, 16, 9, 0, 0, TimeSpan.Zero));

    private static JsonNode T(string id, string title, string? due, string progress = "needs-action", int priority = 0, string list = "work")
    {
        var dueField = due is null ? string.Empty : ",\"due\":\"" + due + "T00:00:00\"";
        return JsonNode.Parse("{\"id\":\"" + id + "\",\"@type\":\"Task\",\"title\":\"" + title + "\",\"progress\":\"" + progress + "\",\"priority\":" + priority.ToString(CultureInfo.InvariantCulture) + ",\"calendarIds\":{\"" + list + "\":true},\"showWithoutTime\":true" + dueField + "}")!;
    }

    private TasksViewModel Vm()
    {
        _source.Events =
        [
            T("a", "Pay rent", "2026-11-10"),
            T("b", "Call Ann", "2026-11-16", priority: 1),
            T("c", "Plan trip", "2026-11-20"),
            T("d", "Old thing", "2026-11-01", "completed"),
            T("e", "No date", null),
            T("f", "Home chore", "2026-11-16", list: "home"),
            JsonNode.Parse("{\"id\":\"ev\",\"@type\":\"Event\",\"title\":\"Not a task\",\"start\":\"2026-11-16T10:00:00\",\"duration\":\"PT1H\",\"calendarIds\":{\"work\":true}}")!,
        ];
        var vm = new TasksViewModel(_source, _writer, _editors, null, null, _time, null, null, null);
        vm.Rebuild();
        return vm;
    }

    [Fact]
    public void Rows_are_open_first_then_due_priority_title_and_events_are_not_tasks()
    {
        var vm = Vm();
        Assert.Equal("a,b,f,c,e,d", string.Join(',', vm.Rows.Select(r => r.Id)));
        Assert.Equal("Overdue · Nov 10", vm.Rows[0].Due);
        Assert.Equal(2, vm.Rows[0].DueRank);
        Assert.Equal("Today", vm.Rows[1].Due);
        Assert.Equal(1, vm.Rows[1].Priority);
        Assert.Equal("Work", vm.Rows[0].ListName);
    }

    [Fact]
    public void Filters_list_and_count_all_pending_overdue_completed()
    {
        var vm = Vm();
        Assert.Equal([6, 5, 1, 1], vm.Counts);
        vm.Filter = 1;
        Assert.Equal(5, vm.Rows.Count);
        vm.Filter = 2;
        Assert.Equal("a", string.Join(',', vm.Rows.Select(r => r.Id)));
        vm.Filter = 3;
        Assert.Equal("d", string.Join(',', vm.Rows.Select(r => r.Id)));
    }

    [Fact]
    public void The_day_filter_of_the_mini_month_keeps_tasks_due_that_day()
    {
        var vm = Vm();
        vm.Day = new DateOnly(2026, 11, 16);
        Assert.Equal("b,f", string.Join(',', vm.Rows.Select(r => r.Id)));
        Assert.Equal("Due Mon, Nov 16", vm.DayLabel);
        vm.Day = null;
        Assert.Equal(6, vm.Rows.Count);
    }

    [Fact]
    public void Hidden_calendars_leave_their_tasks_out()
    {
        _source.Events = [T("a", "A", null), T("h", "H", null, list: "home")];
        var vm = new TasksViewModel(_source, _writer, _editors, null, null, _time, null, () => new HashSet<string>(["home"], StringComparer.Ordinal), null);
        vm.Rebuild();
        Assert.Equal("a", string.Join(',', vm.Rows.Select(r => r.Id)));
    }

    [Fact]
    public async Task Selecting_is_not_opening_and_enter_opens_the_task_editor()
    {
        var vm = Vm();
        vm.Select("b");
        Assert.Equal("b", vm.SelectedId);
        Assert.Empty(_editors.Opened);
        vm.MoveSelection(1);
        Assert.Equal("f", vm.SelectedId);
        await vm.OpenAsync("f", CancellationToken.None);
        Assert.Equal(["task:f"], _editors.Opened);
    }

    [Fact]
    public async Task Space_toggles_progress_and_a_done_task_goes_back_to_needs_action()
    {
        var vm = Vm();
        await vm.ToggleAsync("a", CancellationToken.None);
        await vm.ToggleAsync("d", CancellationToken.None);
        Assert.Equal("completed", (string?)_writer.Updates[0].Patch["progress"]);
        Assert.Equal("needs-action", (string?)_writer.Updates[1].Patch["progress"]);
    }

    [Fact]
    public async Task Pick_date_and_the_due_menu_patch_the_due_date()
    {
        var vm = Vm();
        await vm.SetDueByNameAsync("a", "tomorrow", CancellationToken.None);
        Assert.Equal("2026-11-17T00:00:00", (string?)_writer.Updates[0].Patch["due"]);
        await vm.SetDueByNameAsync("a", "next-week", CancellationToken.None);
        Assert.Equal("2026-11-23T00:00:00", (string?)_writer.Updates[1].Patch["due"]);
        await vm.SetDueByNameAsync("a", "none", CancellationToken.None);
        Assert.True(_writer.Updates[2].Patch.ContainsKey("due") && _writer.Updates[2].Patch["due"] is null);
    }

    [Fact]
    public async Task Move_to_list_replaces_the_calendar_of_the_task()
    {
        var vm = Vm();
        await vm.MoveToListAsync("a", "home", CancellationToken.None);
        Assert.True((bool?)_writer.Updates[0].Patch["calendarIds"]!["home"]);
    }

    [Fact]
    public async Task Quick_add_takes_the_date_words_off_the_title_and_goes_to_the_default_list()
    {
        var vm = Vm();
        vm.Quick = "Buy milk tomorrow";
        Assert.Equal("Due Tue, Nov 17", vm.QuickDueLabel);
        Assert.True(await vm.QuickAddAsync(CancellationToken.None));
        var (list, data) = _writer.Creates[0];
        Assert.Equal("work", list);
        Assert.Equal("Task", (string?)data["@type"]);
        Assert.Equal("Buy milk", (string?)data["title"]);
        Assert.Equal("2026-11-17T00:00:00", (string?)data["due"]);
        Assert.Equal(string.Empty, vm.Quick);

        vm.Quick = "Read a book";
        await vm.QuickAddAsync(CancellationToken.None);
        Assert.Equal("Read a book", (string?)_writer.Creates[1].Data["title"]);
        Assert.False(_writer.Creates[1].Data.ContainsKey("due"));
    }

    [Fact]
    public void The_sidebar_lists_at_most_six_open_tasks_and_counts_the_rest()
    {
        _source.Events = [.. Enumerable.Range(0, 9).Select(i => T("t" + i, "Task " + i, "2026-11-2" + (i % 9).ToString(CultureInfo.InvariantCulture)))];
        var vm = new TasksViewModel(_source, _writer, _editors, null, null, _time, null, null, null);
        vm.Rebuild();
        Assert.Equal(6, vm.Sidebar.Count);
        Assert.Equal(3, vm.SidebarHidden);
    }

    [Fact]
    public void Thousands_of_tasks_rebuild_fast_and_in_one_notification()
    {
        static string Two(int n) => n.ToString("00", CultureInfo.InvariantCulture);
        _source.Events = [.. Enumerable.Range(0, 20_000).Select(i => T("t" + i.ToString(CultureInfo.InvariantCulture), "Task " + i.ToString(CultureInfo.InvariantCulture), "2026-" + Two(1 + (i % 12)) + "-" + Two(1 + (i % 28)), i % 3 == 0 ? "completed" : "needs-action", i % 5))];
        var vm = new TasksViewModel(_source, _writer, _editors, null, null, _time, null, null, null);
        var resets = 0;
        vm.Rows.CollectionChanged += (_, _) => resets++;
        var sw = Stopwatch.StartNew();
        vm.Rebuild();
        sw.Stop();
        Assert.Equal(20_000, vm.Rows.Count);
        Assert.Equal(1, resets);
        Assert.True(sw.ElapsedMilliseconds < 3000, $"rebuild took {sw.ElapsedMilliseconds} ms");
        Assert.Equal(6, vm.Sidebar.Count);
    }

    [Fact]
    public void A_store_change_rebuilds_the_rows()
    {
        var vm = Vm();
        _source.Events = [T("z", "Only", null)];
        _source.Raise();
        Assert.Equal("z", string.Join(',', vm.Rows.Select(r => r.Id)));
    }
}
