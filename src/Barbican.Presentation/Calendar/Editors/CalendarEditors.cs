// Mirrors: Rust cal_editor.rs:512 open_new, :554 open_existing, cal_extra.rs:315 open_task, :1115 scope_go; Bulwark components/calendar/event-modal.tsx, quick-create-popover.tsx, task-modal.tsx, recurrence-scope-dialog.tsx; I-300, I-314, I-322..I-324, I-333..I-339, I-342.
// The default ICalendarEditors: it builds the card view models and puts them on the calendar's card layer (<see cref="CalendarEditorHost"/>); the views draw them.

using Barbican.Core.Calendar;
using Barbican.Core.Calendar.Editing;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Storage;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Calendar.Tasks;

namespace Barbican.Presentation.Calendar.Editors;

/// <summary>The default <see cref="ICalendarEditors"/>.</summary>
public sealed class CalendarEditors : ICalendarEditors
{
    private readonly IEditorSource _source;
    private readonly ICalendarWriter _writer;
    private readonly ICalendarRules _rules;
    private readonly CalendarEditorHost _host;
    private readonly IAvailabilityService? _availability;
    private readonly IContactCardReader? _contacts;
    private readonly IHookBus? _hooks;
    private readonly IToastService? _toasts;
    private readonly TimeProvider _time;
    private readonly IPrefsStore? _prefs;
    private readonly ICalendarParticipantIdentities? _participantIdentities;

    public CalendarEditors(
        IEditorSource source,
        ICalendarWriter writer,
        ICalendarRules rules,
        CalendarEditorHost host,
        IAvailabilityService? availability = null,
        IContactCardReader? contacts = null,
        IHookBus? hooks = null,
        IToastService? toasts = null,
        TimeProvider? time = null,
        IPrefsStore? prefs = null,
        ICalendarParticipantIdentities? participantIdentities = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(host);
        _source = source;
        _writer = writer;
        _rules = rules;
        _host = host;
        _availability = availability;
        _contacts = contacts;
        _hooks = hooks;
        _toasts = toasts;
        _time = time ?? TimeProvider.System;
        _prefs = prefs;
        _participantIdentities = participantIdentities;
    }

    /// <summary>The card layer.</summary>
    public CalendarEditorHost Host => _host;

    private string DefaultZone => Barbican.Core.Settings.Pages.SettingDisplayTimeZone.EventZone(_prefs?.Current.Str("timeZone", "auto"), _time.LocalTimeZone);

    private bool TwelveHour => _prefs is not null && CalendarSettings.Read(_prefs.Current).TwelveHour;

    private EventEditorServices Services() => new(_source, _writer, _rules, _host, _availability, _contacts, _hooks, _toasts, _time, TwelveHour, _participantIdentities, DefaultZone);

    public Task OpenQuickCreateAsync(NewEventRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var card = new QuickCreateViewModel(_source, _writer, request, TwelveHour, DefaultZone);
        _host.Show(card);
        _ = FinishQuickAsync(card, ct);
        return Task.CompletedTask;
    }

    private async Task FinishQuickAsync(QuickCreateViewModel card, CancellationToken ct)
    {
#pragma warning disable VSTHRD003 // completed by the card itself
        await card.Closed.ConfigureAwait(true);
#pragma warning restore VSTHRD003
        _host.Close(card);
        if (card.MoreRequest is { } more)
        {
            await OpenNewEventAsync(more, ct).ConfigureAwait(true);
        }
    }

    public Task OpenNewEventAsync(NewEventRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var editor = new EventEditorViewModel(Services());
        editor.OpenNew(request);
        Show(editor);
        return Task.CompletedTask;
    }

    public Task OpenAsync(string occurrenceId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(occurrenceId);
        var id = CalendarJson.BaseId(occurrenceId);
        var ev = _source.Events.FirstOrDefault(e => string.Equals(CalendarJson.Id(e), id, StringComparison.Ordinal));
        if (ev is null)
        {
            return Task.CompletedTask;
        }

        if (occurrenceId.StartsWith("task:", StringComparison.Ordinal) || CalendarJson.IsTask(ev))
        {
            var task = new TaskEditorViewModel(_source, _writer, _host, DefaultZone);
            task.OpenExisting(ev);
            ShowTask(task);
            return Task.CompletedTask;
        }

        var editor = new EventEditorViewModel(Services());
        editor.OpenExisting(ev, CalendarJson.RecurrenceIdOf(occurrenceId));
        Show(editor);
        return Task.CompletedTask;
    }

    public Task OpenNewTaskAsync(string? calendarId, DateOnly? due, CancellationToken ct)
    {
        var task = new TaskEditorViewModel(_source, _writer, _host, DefaultZone);
        task.OpenNew(calendarId, due);
        ShowTask(task);
        return Task.CompletedTask;
    }

    public Task<(EditScope Scope, bool Notify)?> AskScopeAsync(string title, bool forDelete, bool hasGuests, CancellationToken ct) => _host.AskScopeAsync(title, forDelete, hasGuests, ct);

    /// <summary>Opens a copy of the editor's event as a new event (the footer's Duplicate, the event menu's Duplicate).</summary>
    public void OpenDuplicate(EventEditorViewModel original)
    {
        ArgumentNullException.ThrowIfNull(original);
        original.Close();
        Show(original.DuplicateCopy());
    }

    /// <summary>A transient editor over an existing event, for what the menus do with an event without opening the card (delete with its scope question); null for a task or an unknown id.</summary>
    public EventEditorViewModel? EditorFor(string occurrenceId)
    {
        ArgumentNullException.ThrowIfNull(occurrenceId);
        var ev = _source.Events.FirstOrDefault(e => string.Equals(CalendarJson.Id(e), CalendarJson.BaseId(occurrenceId), StringComparison.Ordinal));
        if (ev is null || CalendarJson.IsTask(ev))
        {
            return null;
        }

        var editor = new EventEditorViewModel(Services());
        editor.OpenExisting(ev, CalendarJson.RecurrenceIdOf(occurrenceId));
        return editor;
    }

    /// <summary>The event menu's Duplicate: a copy of the event in the editor as a new event.</summary>
    public void OpenDuplicateOf(string occurrenceId)
    {
        if (EditorFor(occurrenceId) is { } editor)
        {
            Show(editor.DuplicateCopy());
        }
    }

    private void Show(EventEditorViewModel editor)
    {
        _host.Show(editor);
        _ = CloseWhenDoneAsync(editor, editor.Closed);
    }

    private void ShowTask(TaskEditorViewModel task)
    {
        _host.Show(task);
        _ = CloseWhenDoneAsync(task, task.Closed);
    }

    private async Task CloseWhenDoneAsync(object card, Task closed)
    {
#pragma warning disable VSTHRD003 // completed by the card itself
        await closed.ConfigureAwait(true);
#pragma warning restore VSTHRD003
        _host.Close(card);
    }
}
