// Mirrors: Rust cal_extra.rs:268 parse_task_due, :301 task_quick_add, render_tasks_view (filters with counts, the day filter of the mini month, sort: open first, then due, priority, title), calendar.rs:830 toggle_task, :921 set_task_due, :755 sidebar_tasks;
// Bulwark components/calendar/task-list-view.tsx, task-toolbar.tsx, tasks-sidebar-filters.tsx, task-context-menu.tsx; B-CAL-007, 036..039; I-342..I-344; PARITY-ROUND2 (the Tasks view with thousands of tasks crashed: rows are built in one pass and the view virtualises them).

using System.Globalization;
using System.Text.Json.Nodes;
using Barbican.Core.Calendar.Editing;
using Barbican.Core.Calendar.Tasks;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Calendar.Editors;
using Barbican.Presentation.Common;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Barbican.Presentation.Calendar.Tasks;

/// <summary>The Tasks view and the capped sidebar list over the tasks of the active login.</summary>
public sealed class TasksViewModel : ObservableObject
{
    /// <summary>The filters in index order.</summary>
    public static IReadOnlyList<string> FilterNames { get; } = ["All", "Pending", "Overdue", "Completed"];

    private readonly IEditorSource _source;
    private readonly ICalendarWriter _writer;
    private readonly ICalendarEditors _editors;
    private readonly IHookBus? _hooks;
    private readonly IToastService? _toasts;
    private readonly TimeProvider _time;
    private readonly Func<bool> _twelveHour;
    private readonly Func<IReadOnlySet<string>> _hidden;
    private readonly CalendarEditorHost? _host;
    private List<TaskRow> _all = [];
    private int _filter;
    private DateOnly? _day;
    private string _quick = string.Empty;
    private string? _selectedId;
    private IReadOnlyList<int> _counts = [0, 0, 0, 0];
    private IReadOnlyList<TaskRow> _sidebar = [];
    private int _sidebarHidden;

    public TasksViewModel(
        IEditorSource source,
        ICalendarWriter writer,
        ICalendarEditors editors,
        IHookBus? hooks = null,
        IToastService? toasts = null,
        TimeProvider? time = null,
        Func<bool>? twelveHour = null,
        Func<IReadOnlySet<string>>? hiddenCalendars = null,
        CalendarEditorHost? host = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(editors);
        _source = source;
        _writer = writer;
        _editors = editors;
        _hooks = hooks;
        _toasts = toasts;
        _time = time ?? TimeProvider.System;
        _twelveHour = twelveHour ?? (() => false);
        _hidden = hiddenCalendars ?? (() => new HashSet<string>(StringComparer.Ordinal));
        _host = host;
        _source.Changed += (_, _) => Rebuild();
    }

    /// <summary>The rows the view shows (filtered and sorted); replaced as a whole, so the view is told once.</summary>
    public BulkObservableCollection<TaskRow> Rows { get; } = [];

    /// <summary>All, pending, overdue and completed counts of the visible tasks.</summary>
    public IReadOnlyList<int> Counts
    {
        get => _counts;
        private set => SetProperty(ref _counts, value);
    }

    /// <summary>0 all, 1 pending, 2 overdue, 3 completed.</summary>
    public int Filter
    {
        get => _filter;
        set
        {
            var v = Math.Clamp(value, 0, 3);
            if (!SetProperty(ref _filter, v))
            {
                return;
            }

            Rebuild();
            if (_hooks is { } h && h.HasHandlers(HookNames.OnTaskFilterChange))
            {
                _ = h.NotifyAsync(HookNames.OnTaskFilterChange, new TaskFilterPayload(FilterNames[v].ToLowerInvariant(), v is 0 or 3), CancellationToken.None).AsTask();
            }
        }
    }

    /// <summary>The day picked in the mini month (only tasks due that day), or null.</summary>
    public DateOnly? Day
    {
        get => _day;
        set
        {
            if (SetProperty(ref _day, value))
            {
                OnPropertyChanged(nameof(DayLabel));
                Rebuild();
            }
        }
    }

    /// <summary>"Due Fri, Nov 20" while a day filter is on.</summary>
    public string DayLabel => _day is { } d ? "Due " + d.ToString("ddd, MMM d", CultureInfo.InvariantCulture) : string.Empty;

    /// <summary>The quick-add field.</summary>
    public string Quick
    {
        get => _quick;
        set
        {
            if (SetProperty(ref _quick, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(QuickDueLabel));
            }
        }
    }

    /// <summary>"Due Fri, Nov 20" for the date words at the end of the quick-add text, else empty.</summary>
    public string QuickDueLabel => TaskRules.ParseDue(_quick, Today()) is { } p ? "Due " + p.Due.ToString("ddd, MMM d", CultureInfo.InvariantCulture) : string.Empty;

    public string? SelectedId
    {
        get => _selectedId;
        private set => SetProperty(ref _selectedId, value);
    }

    /// <summary>The capped list under the mini month: at most <c>TaskRules.SidebarTasks</c> open tasks, soonest due first.</summary>
    public IReadOnlyList<TaskRow> Sidebar
    {
        get => _sidebar;
        private set => SetProperty(ref _sidebar, value);
    }

    /// <summary>Open tasks left out of <see cref="Sidebar"/> ("Show all (N)").</summary>
    public int SidebarHidden
    {
        get => _sidebarHidden;
        private set => SetProperty(ref _sidebarHidden, value);
    }

    public bool IsEmpty => Rows.Count == 0;

    private DateOnly Today() => DateOnly.FromDateTime(_time.GetLocalNow().DateTime);

    /// <summary>Reads the tasks of the store again (the store changed, the filter or the day changed, a calendar was hidden).</summary>
    public void Rebuild()
    {
        var today = Today();
        var (all, counts) = ReadTasks(today);
        var sorted = all
            .OrderBy(t => t.Row.Finished)
            .ThenBy(t => t.Sort, StringComparer.Ordinal)
            .ThenBy(t => t.Pri)
            .ThenBy(t => t.Row.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var visible = sorted
            .Where(t => TaskRules.Matches(t.Task, _filter, today) && (_day is not { } dd || t.Row.DueDay == dd))
            .Select(t => t.Row)
            .ToList();
        _all = [.. sorted.Select(t => t.Row)];
        Rows.ReplaceAll(visible);
        Counts = counts;
        var (shown, left) = TaskRules.Sidebar([.. sorted.Select(t => t.Task)]);
        Sidebar = [.. _all.Where(r => !r.Done).Take(shown)];
        SidebarHidden = left;
        if (_selectedId is { } sel && !visible.Any(r => string.Equals(r.Id, sel, StringComparison.Ordinal)))
        {
            SelectedId = null;
        }

        OnPropertyChanged(nameof(IsEmpty));
    }

    private (List<(TaskRow Row, string Sort, int Pri, JsonNode Task)> All, int[] Counts) ReadTasks(DateOnly today)
    {
        var twelve = _twelveHour();
        var hidden = _hidden();
        var names = new Dictionary<string, (string Name, string Color)>(StringComparer.Ordinal);
        foreach (var c in _source.Calendars)
        {
            names[CalendarJson.Id(c)] = (CalendarJson.Str(c, "name"), CalendarJson.Str(c, "color") is { Length: > 0 } col ? col : "#3b82f6");
        }

        var counts = new int[4];
        var all = new List<(TaskRow Row, string Sort, int Pri, JsonNode Task)>();
        foreach (var e in _source.Events)
        {
            if (!CalendarJson.IsTask(e) || hidden.Contains(CalendarJson.FirstCalendar(e)))
            {
                continue;
            }

            for (var f = 0; f < 4; f++)
            {
                if (TaskRules.Matches(e, f, today))
                {
                    counts[f]++;
                }
            }

            var priority = e["priority"] is JsonValue pv && pv.TryGetValue<long>(out var pl) ? pl : 0;
            all.Add((RowOf(e, today, twelve, names), CalendarJson.Str(e, "due") is { Length: > 0 } ds ? ds : "9999", priority == 0 ? 10 : (int)priority, e));
        }

        return (all, counts);
    }

    private static TaskRow RowOf(JsonNode e, DateOnly today, bool twelve, Dictionary<string, (string Name, string Color)> names)
    {
        var list = CalendarJson.FirstCalendar(e);
        var (label, rank) = TaskRules.DueLabel(e, today, twelve);
        var priority = e["priority"] is JsonValue pv && pv.TryGetValue<long>(out var pl) ? pl : 0;
        var title = CalendarJson.Str(e, "title");
        var notes = CalendarJson.Str(e, "description");
        var nl = notes.AsSpan().IndexOfAny((char)13, (char)10);
        names.TryGetValue(list, out var meta);
        var due = CalendarJson.ParseLocal(CalendarJson.Str(e, "due"));
        return new TaskRow(
            CalendarJson.Id(e),
            title.Length > 0 ? title : "(untitled task)",
            nl < 0 ? notes : notes[..nl],
            label,
            rank,
            string.Equals(CalendarJson.Str(e, "progress"), "completed", StringComparison.Ordinal),
            TaskRules.Finished(e),
            TaskRules.PriorityLevel(priority),
            list,
            meta.Name ?? string.Empty,
            meta.Color ?? "#3b82f6",
            due is { } d ? DateOnly.FromDateTime(d) : null);
    }

    public void Select(string? id) => SelectedId = id;

    /// <summary>Moves the selection by <paramref name="delta"/> rows (Up and Down keys).</summary>
    public void MoveSelection(int delta)
    {
        if (Rows.Count == 0)
        {
            return;
        }

        var index = _selectedId is null ? -1 : Rows.ToList().FindIndex(r => string.Equals(r.Id, _selectedId, StringComparison.Ordinal));
        SelectedId = Rows[Math.Clamp(index + delta, 0, Rows.Count - 1)].Id;
    }

    /// <summary>Enter or double click: the task editor.</summary>
    public Task OpenAsync(string id, CancellationToken ct) => _editors.OpenAsync("task:" + id, ct);

    /// <summary>Space or the checkbox: completed and needs-action swap.</summary>
    public async Task ToggleAsync(string id, CancellationToken ct)
    {
        if (_source.AccountKey is not { } account || Find(id) is not { } row)
        {
            return;
        }

        var patch = new JsonObject { ["progress"] = row.Done ? "needs-action" : "completed" };
        await ReportAsync(await _writer.UpdateAsync(account, id, patch, ct).ConfigureAwait(true), "Task not updated").ConfigureAwait(true);
    }

    /// <summary>Pick date / Due date: a day, or null to clear. A timed task keeps its time.</summary>
    public async Task SetDueAsync(string id, DateOnly? day, CancellationToken ct)
    {
        if (_source.AccountKey is not { } account)
        {
            return;
        }

        var task = _source.Events.FirstOrDefault(e => string.Equals(CalendarJson.Id(e), id, StringComparison.Ordinal));
        var patch = new JsonObject();
        if (day is not { } d)
        {
            patch["due"] = null;
            patch["alerts"] = null;
        }
        else
        {
            var old = task is null ? null : CalendarJson.ParseLocal(CalendarJson.Str(task, "due"));
            var timed = task is not null && !CalendarJson.Flag(task, "showWithoutTime") && old is { } o && o.TimeOfDay != TimeSpan.Zero;
            var time = timed ? old!.Value.TimeOfDay : TimeSpan.Zero;
            patch["due"] = CalendarJson.FormatLocal(d.ToDateTime(TimeOnly.MinValue) + time);
            patch["showWithoutTime"] = !timed;
        }

        await ReportAsync(await _writer.UpdateAsync(account, id, patch, ct).ConfigureAwait(true), "Task not updated").ConfigureAwait(true);
    }

    /// <summary>"Today", "Tomorrow", "Next week" or "No due date" from the task menu.</summary>
    public Task SetDueByNameAsync(string id, string name, CancellationToken ct)
    {
        var today = Today();
        DateOnly? day = name switch
        {
            "today" => today,
            "tomorrow" => today.AddDays(1),
            "next-week" => today.AddDays(7),
            _ => null,
        };
        return SetDueAsync(id, day, ct);
    }

    /// <summary>Move to list: the task's calendar becomes <paramref name="listId"/>.</summary>
    public async Task MoveToListAsync(string id, string listId, CancellationToken ct)
    {
        if (_source.AccountKey is not { } account)
        {
            return;
        }

        var patch = new JsonObject { ["calendarIds"] = new JsonObject { [listId] = true } };
        await ReportAsync(await _writer.UpdateAsync(account, id, patch, ct).ConfigureAwait(true), "Task not moved").ConfigureAwait(true);
    }

    /// <summary>Delete (the task menu, the Delete key): asks first.</summary>
    public async Task DeleteAsync(string id, CancellationToken ct)
    {
        if (_source.AccountKey is not { } account || Find(id) is not { } row)
        {
            return;
        }

        if (_host is not null && !await _host.ConfirmAsync("delete-task", "Delete task?", $"\"{row.Title}\" will be deleted.", "Delete", true, ct).ConfigureAwait(true))
        {
            return;
        }

        await ReportAsync(await _writer.DestroyAsync(account, id, ct).ConfigureAwait(true), "Task not deleted").ConfigureAwait(true);
    }

    /// <summary>Quick add: "Buy milk tomorrow" adds a task "Buy milk" due tomorrow in the default list. False when the text is empty.</summary>
    public async Task<bool> QuickAddAsync(CancellationToken ct)
    {
        var text = _quick.Trim();
        if (text.Length == 0 || _source.AccountKey is not { } account || DefaultList() is not { } list)
        {
            return false;
        }

        var parsed = TaskRules.ParseDue(text, Today());
        var title = parsed?.Title ?? text;
        var due = parsed is { } p ? p.Due.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : string.Empty;
        var fields = TaskRules.Fields(title, string.Empty, due, null, 0, 0, 0, 0, CalendarJson.LocalZoneId());
        if (!fields.IsOk)
        {
            return false;
        }

        var data = Clean(fields.Value!);
        var result = await _writer.CreateAsync(account, list, data, ct).ConfigureAwait(true);
        if (!await ReportAsync(result, "Task not added").ConfigureAwait(true))
        {
            return false;
        }

        Quick = string.Empty;
        return true;
    }

    /// <summary>A task as a create sends it: type and uid added, nulls dropped (Rust task_save).</summary>
    public static JsonObject Clean(JsonObject fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var data = new JsonObject { ["@type"] = "Task", ["uid"] = Guid.NewGuid().ToString("N") + "@barbican" };
        foreach (var (k, v) in fields)
        {
            if (v is not null)
            {
                data[k] = v.DeepClone();
            }
        }

        return data;
    }

    /// <summary>The list new tasks go to: the default calendar of the login, else the first own one (Rust default_task_list).</summary>
    public string? DefaultList()
    {
        var own = _source.Calendars.Where(c => !CalendarJson.IsShared(CalendarJson.Id(c)) && !_source.Subscriptions.Contains(CalendarJson.Id(c))).ToList();
        return (own.FirstOrDefault(c => CalendarJson.Flag(c, "isDefault")) ?? own.FirstOrDefault()) is { } c ? CalendarJson.Id(c) : null;
    }

    /// <summary>The lists a task can move to.</summary>
    public IReadOnlyList<CalendarChoice> Lists => [.. _source.Calendars
        .Where(c => !CalendarJson.IsShared(CalendarJson.Id(c)) && !_source.Subscriptions.Contains(CalendarJson.Id(c)))
        .Select(c => new CalendarChoice(CalendarJson.Id(c), CalendarJson.Str(c, "name"), CalendarJson.Str(c, "color")))];

    private TaskRow? Find(string id) => _all.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.Ordinal));

    private async Task<bool> ReportAsync(Barbican.Core.Common.Result<Barbican.Core.Common.Unit> result, string message)
    {
        if (result.IsOk)
        {
            return true;
        }

        _toasts?.Show(new ToastRequest(message + ": " + result.Error!.Message, true));
        await Task.CompletedTask.ConfigureAwait(true);
        return false;
    }

    private async Task<bool> ReportAsync(Barbican.Core.Common.Result<string> result, string message)
    {
        if (result.IsOk)
        {
            return true;
        }

        _toasts?.Show(new ToastRequest(message + ": " + result.Error!.Message, true));
        await Task.CompletedTask.ConfigureAwait(true);
        return false;
    }
}
