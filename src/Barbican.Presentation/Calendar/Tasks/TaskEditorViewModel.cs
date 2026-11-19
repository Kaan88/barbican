// Mirrors: Rust cal_extra.rs:315 open_task, :361 task_fields, :401 task_save, :425 task_delete; Bulwark components/calendar/task-modal.tsx (title, notes, due date and time, priority, progress, repeat, alert, list); B-CAL-036, 038, 039; I-342.
// The task editor as an in-pane card, saved through TaskRules.Fields and ICalendarWriter.

using System.Globalization;
using System.Text.Json.Nodes;
using Barbican.Core.Calendar.Editing;
using Barbican.Core.Calendar.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Barbican.Presentation.Calendar.Tasks;

/// <summary>A new task or an existing one.</summary>
public sealed class TaskEditorViewModel : ObservableObject
{
    public static IReadOnlyList<string> PriorityChoices { get; } = ["None", "High", "Medium", "Low"];

    public static IReadOnlyList<string> ProgressChoices { get; } = ["Needs action", "In progress", "Completed", "Cancelled"];

    public static IReadOnlyList<string> RepeatChoices { get; } = ["Does not repeat", "Daily", "Weekly", "Monthly", "Yearly"];

    public static IReadOnlyList<string> AlertChoices { get; } = ["No alert", "At due time", "5 minutes before", "15 minutes before", "30 minutes before", "1 hour before", "1 day before"];

    private readonly Editors.IEditorSource _source;
    private readonly ICalendarWriter _writer;
    private readonly Editors.CalendarEditorHost? _host;
    private string _defaultZone;
    private readonly Func<string>? _currentZone;
    private string? _existingZone;
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string? _id;
    private string _title = string.Empty;
    private string _notes = string.Empty;
    private string _dueDate = string.Empty;
    private bool _hasTime;
    private string _dueTime = "09:00";
    private int _priority;
    private int _progress;
    private int _repeat;
    private int _alert;
    private string _listId = string.Empty;
    private string _error = string.Empty;

    public TaskEditorViewModel(Editors.IEditorSource source, ICalendarWriter writer, Editors.CalendarEditorHost? host = null, string? defaultZone = null, Func<string>? currentZone = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(writer);
        _source = source;
        _writer = writer;
        _host = host;
        _defaultZone = defaultZone ?? Editors.CalendarJson.LocalZoneId();
        _currentZone = currentZone;
        Lists = [.. source.Calendars
            .Where(c => !Editors.CalendarJson.IsShared(Editors.CalendarJson.Id(c)) && !source.Subscriptions.Contains(Editors.CalendarJson.Id(c)))
            .Select(c => new Editors.CalendarChoice(Editors.CalendarJson.Id(c), Editors.CalendarJson.Str(c, "name") is { Length: > 0 } n ? n : "Calendar", Editors.CalendarJson.Str(c, "color") is { Length: > 0 } col ? col : "#3b82f6"))];
    }

#pragma warning disable VSTHRD003 // the source is completed by the editor itself
    public Task Closed => _closed.Task;
#pragma warning restore VSTHRD003

    public IReadOnlyList<Editors.CalendarChoice> Lists { get; }

    public string TimeZone
    {
        get => _defaultZone;
        set { if (SetProperty(ref _defaultZone, value ?? string.Empty)) Error = string.Empty; }
    }

    public bool IsNew => _id is null;

    public string? Id => _id;

    public string Heading => IsNew ? "New task" : "Edit task";

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value ?? string.Empty);
    }

    public string Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value ?? string.Empty);
    }

    /// <summary>"yyyy-MM-dd", or empty for no due date.</summary>
    public string DueDate
    {
        get => _dueDate;
        set => SetProperty(ref _dueDate, value ?? string.Empty);
    }

    public bool HasTime
    {
        get => _hasTime;
        set => SetProperty(ref _hasTime, value);
    }

    public string DueTime
    {
        get => _dueTime;
        set => SetProperty(ref _dueTime, value ?? string.Empty);
    }

    /// <summary>0 none, 1 high, 2 medium, 3 low.</summary>
    public int Priority
    {
        get => _priority;
        set => SetProperty(ref _priority, Math.Clamp(value, 0, 3));
    }

    /// <summary>0 needs action, 1 in progress, 2 completed, 3 cancelled.</summary>
    public int Progress
    {
        get => _progress;
        set => SetProperty(ref _progress, Math.Clamp(value, 0, 3));
    }

    public int Repeat
    {
        get => _repeat;
        set => SetProperty(ref _repeat, Math.Clamp(value, 0, 4));
    }

    /// <summary>An index into <see cref="TaskRules.AlertMinutes"/>.</summary>
    public int Alert
    {
        get => _alert;
        set => SetProperty(ref _alert, Math.Clamp(value, 0, 6));
    }

    public string ListId
    {
        get => _listId;
        set => SetProperty(ref _listId, value ?? string.Empty);
    }

    public string Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    /// <summary>A new task, optionally with a due day and a list.</summary>
    public void OpenNew(string? calendarId, DateOnly? due)
    {
        _id = null;
        _existingZone = null;
        _listId = Lists.Any(l => string.Equals(l.Id, calendarId, StringComparison.Ordinal)) ? calendarId! : DefaultList();
        _dueDate = due is { } d ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : string.Empty;
        OnPropertyChanged(string.Empty);
    }

    /// <summary>An existing task (Rust open_task).</summary>
    public void OpenExisting(JsonNode task)
    {
        ArgumentNullException.ThrowIfNull(task);
        _id = Editors.CalendarJson.Id(task);
        _existingZone = Editors.CalendarJson.Str(task, "timeZone") is { Length: > 0 } zone ? zone : null;
        _title = Editors.CalendarJson.Str(task, "title");
        _notes = Editors.CalendarJson.Str(task, "description");
        var due = Editors.CalendarJson.ParseLocal(Editors.CalendarJson.Str(task, "due"));
        _dueDate = due is { } d ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : string.Empty;
        _hasTime = due is { } dt && !Editors.CalendarJson.Flag(task, "showWithoutTime") && (dt.TimeOfDay != TimeSpan.Zero || task["showWithoutTime"] is JsonValue);
        _dueTime = _hasTime ? due!.Value.ToString("HH:mm", CultureInfo.InvariantCulture) : "09:00";
        _priority = TaskRules.PriorityLevel(task["priority"] is JsonValue pv && pv.TryGetValue<long>(out var pl) ? pl : 0);
        _progress = Editors.CalendarJson.Str(task, "progress") switch { "in-process" => 1, "completed" => 2, "cancelled" => 3, _ => 0 };
        var list = Editors.CalendarJson.FirstCalendar(task);
        _listId = Lists.Any(l => string.Equals(l.Id, list, StringComparison.Ordinal)) ? list : DefaultList();
        _repeat = task["recurrenceRules"] is JsonArray { Count: > 0 } r ? Math.Max(0, Array.IndexOf(["daily", "weekly", "monthly", "yearly"], Editors.CalendarJson.Str(r[0], "frequency")) + 1) : 0;
        _alert = AlertIndex(task);
        OnPropertyChanged(string.Empty);
    }

    /// <summary>Saves (create or edit). False with <see cref="Error"/> set when the title is empty, a date is invalid or the write failed.</summary>
    public async Task<bool> SaveAsync(CancellationToken ct)
    {
        if (_title.Trim().Length == 0)
        {
            Error = "Give the task a title.";
            return false;
        }

        if (_id is null && _hasTime && _dueDate.Length > 0 && _defaultZone.Length == 0)
        {
            _defaultZone = _currentZone?.Invoke() ?? string.Empty;
            if (_defaultZone.Length == 0)
            {
                Error = "The system time zone is unavailable. Choose a supported time zone, then try again.";
                return false;
            }
        }

        var fields = TaskRules.Fields(_title, _notes, _dueDate, _hasTime ? _dueTime : null, _priority, _progress, _repeat, _alert, _existingZone ?? _defaultZone);
        if (!fields.IsOk)
        {
            Error = fields.Error!.Message;
            return false;
        }

        if (_id is not null && _hasTime && _existingZone is null) fields.Value!["timeZone"] = null;

        if (_listId.Length == 0 || _source.AccountKey is not { } account)
        {
            Error = "Choose a list.";
            return false;
        }

        Error = string.Empty;
        if (_id is null)
        {
            var created = await _writer.CreateAsync(account, _listId, TasksViewModel.Clean(fields.Value!), ct).ConfigureAwait(true);
            if (!created.IsOk)
            {
                Error = created.Error!.Message;
                return false;
            }
        }
        else
        {
            var patch = fields.Value!;
            patch["calendarIds"] = new JsonObject { [_listId] = true };
            var updated = await _writer.UpdateAsync(account, _id, patch, ct).ConfigureAwait(true);
            if (!updated.IsOk)
            {
                Error = updated.Error!.Message;
                return false;
            }
        }

        Close();
        return true;
    }

    /// <summary>Delete from the footer: asks first.</summary>
    public async Task<bool> DeleteAsync(CancellationToken ct)
    {
        if (_id is null || _source.AccountKey is not { } account)
        {
            return false;
        }

        if (_host is not null && !await _host.ConfirmAsync("delete-task", "Delete task?", $"\"{_title}\" will be deleted.", "Delete", true, ct).ConfigureAwait(true))
        {
            return false;
        }

        var result = await _writer.DestroyAsync(account, _id, ct).ConfigureAwait(true);
        if (!result.IsOk)
        {
            Error = result.Error!.Message;
            return false;
        }

        Close();
        return true;
    }

    public void Close() => _closed.TrySetResult();

    private string DefaultList() => (Lists.FirstOrDefault(l => _source.Calendars.Any(c => string.Equals(Editors.CalendarJson.Id(c), l.Id, StringComparison.Ordinal) && Editors.CalendarJson.Flag(c, "isDefault"))) ?? (Lists.Count > 0 ? Lists[0] : null))?.Id ?? string.Empty;

    private static int AlertIndex(JsonNode task)
    {
        if (task["alerts"] is not JsonObject { Count: > 0 } alerts)
        {
            return 0;
        }

        var offset = Editors.CalendarJson.Str(alerts.First().Value?["trigger"], "offset");
        if (string.Equals(offset, "PT0S", StringComparison.Ordinal))
        {
            return 1;
        }

        if (offset.StartsWith("-PT", StringComparison.Ordinal) && offset.EndsWith('M') && int.TryParse(offset.AsSpan(3, offset.Length - 4), NumberStyles.None, CultureInfo.InvariantCulture, out var mins))
        {
            var i = TaskRules.AlertMinutes.ToList().IndexOf(mins);
            return i < 0 ? 0 : i;
        }

        return 0;
    }
}
