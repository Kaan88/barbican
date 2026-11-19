// Mirrors: Rust cal_editor.rs:512 open_new, :554 open_existing, :1037 save, :1115 scope_go, :1177 delete; Bulwark event-modal.tsx (handleSave :925-1040 with onCalendarEventFormSave, handleDelete, dirty check, view mode), recurrence-scope-dialog.tsx;
// B-CAL-013..017, 051; I-300, I-330, I-332, I-333, I-339. Opening, closing, saving and deleting the event editor.

using System.Globalization;
using System.Text.Json.Nodes;
using Barbican.Core.Calendar.Editing;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Compose;

namespace Barbican.Presentation.Calendar.Editors;

public sealed partial class EventEditorViewModel
{
    private JsonObject _keptAlerts = [];
    private IReadOnlyList<string> _reservedAlerts = [];

    /// <summary>A new event (a click, a drag, "New event", "More options" of quick create).</summary>
    public void OpenNew(NewEventRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _event = null;
        _occurrence = null;
        _recurrenceId = null;
        LoadCalendars(request.CalendarId);
        _title = request.Title;
        _allDay = request.AllDay;
        _startDate = DateOnly.FromDateTime(request.Start);
        _startTime = request.Start.ToString("HH:mm", CultureInfo.InvariantCulture);
        var end = request.End > request.Start || request.AllDay ? request.End : request.Start.AddHours(1);
        _endDate = DateOnly.FromDateTime(end);
        if (_endDate < _startDate)
        {
            _endDate = _startDate;
        }

        _endTime = end.ToString("HH:mm", CultureInfo.InvariantCulture);
        _timeZone = request.AllDay ? null : _s.DefaultZone ?? CalendarJson.LocalZoneId();
        _freeBusy = request.AllDay ? 1 : 0;
        _useDefaultAlerts = true;
        _notify = true;
        Alerts.Clear();
        Guests.Clear();
        ReadOnly = !Calendars.Any();
        Finish();
    }

    /// <summary>An existing event, or one occurrence of a series (<paramref name="recurrenceId"/>: the occurrence's start on the event's own clock, as in the occurrence id).</summary>
    public void OpenExisting(JsonNode ev, string? recurrenceId)
    {
        ArgumentNullException.ThrowIfNull(ev);
        _event = ev;
        var calendarId = CalendarJson.FirstCalendar(ev);
        LoadCalendars(calendarId);
        if (!Calendars.Any(c => string.Equals(c.Id, calendarId, StringComparison.Ordinal)) && calendarId.Length > 0)
        {
            // a calendar the user may not add to still has to show as the event's own
            Calendars = [.. Calendars, new CalendarChoice(calendarId, CalendarJson.Str(_s.Source.Calendars.FirstOrDefault(c => string.Equals(CalendarJson.Id(c), calendarId, StringComparison.Ordinal)), "name"), "#3b82f6")];
            _calendarId = calendarId;
        }

        _title = CalendarJson.Str(ev, "title");
        _description = _s.Rules.DescriptionText(ev);
        _location = LocationOf(ev);
        _allDay = CalendarJson.Flag(ev, "showWithoutTime");
        _timeZone = _allDay ? null : CalendarJson.Str(ev, "timeZone") is { Length: > 0 } z ? z : null;
        var duration = CalendarJson.ParseDuration(CalendarJson.Str(ev, "duration"));
        var start = CalendarJson.ParseLocal(CalendarJson.Str(ev, "start")) ?? DateTime.Today;
        _recurrenceId = null;
        if (recurrenceId is not null && CalendarJson.ParseLocal(recurrenceId) is { } occStart)
        {
            start = occStart;
            _recurrenceId = recurrenceId;
        }

        var end = _allDay ? start.Date.AddDays(Math.Max(0, Math.Round(duration.TotalDays) - 1)) : start + duration;
        _startDate = DateOnly.FromDateTime(start);
        _startTime = start.ToString("HH:mm", CultureInfo.InvariantCulture);
        _endDate = DateOnly.FromDateTime(end);
        _endTime = end.ToString("HH:mm", CultureInfo.InvariantCulture);
        _occurrence = _recurrenceId is null ? null : (start, end);
        _freeBusy = string.Equals(CalendarJson.Str(ev, "freeBusyStatus"), "free", StringComparison.Ordinal) ? 1 : 0;
        _privacy = CalendarJson.Str(ev, "privacy") switch { "private" => 1, "secret" => 2, _ => 0 };
        LoadRepeat(ev, start);
        LoadAlerts(ev);
        LoadGuests(ev);
        _notify = true;
        var writable = Calendars.Any(c => string.Equals(c.Id, calendarId, StringComparison.Ordinal)) && _s.Source.Calendars.Any(c => string.Equals(CalendarJson.Id(c), calendarId, StringComparison.Ordinal) && CalendarJson.Writable(c, new HashSet<string>(_s.Source.Subscriptions, StringComparer.Ordinal)));
        ReadOnly = !writable || !UserOrganizes;
        Finish();
    }

    private void Finish()
    {
        _customLoaded = _repeat == 5;
        Rebase();
        OnPropertyChanged(string.Empty);

        if (_s.Hooks is { } hooks && hooks.HasHandlers(HookNames.OnCalendarEventFormOpen))
        {
            _ = hooks.NotifyAsync(HookNames.OnCalendarEventFormOpen, FormView(null), CancellationToken.None).AsTask();
        }

        RefreshAvailabilityLater();
    }

    private void LoadRepeat(JsonNode ev, DateTime start)
    {
        _repeat = 0;
        _customLoaded = false;
        if (ev["recurrenceRules"] is not JsonArray { Count: > 0 } rules || rules[0] is not { } first)
        {
            return;
        }

        var rule = _s.Rules.ParseRule(first);
        var simple = Array.IndexOf(["daily", "weekly", "monthly", "yearly"], rule.Freq);
        if (rules.Count == 1 && _s.Rules.IsSimple(rule) && simple >= 0)
        {
            _repeat = simple + 1;
            return;
        }

        _repeat = 5;
        Recurrence.Load(rule, start);
        _customLoaded = true;
    }

    private void LoadAlerts(JsonNode ev)
    {
        Alerts.Clear();
        var own = ev["alerts"] as JsonObject;
        _useDefaultAlerts = CalendarJson.Flag(ev, "useDefaultAlerts") && (own is null || own.Count == 0);
        var (rows, kept) = _s.Rules.AlertRows(own);
        _keptAlerts = kept;
        _reservedAlerts = own?.Select(p => p.Key).ToList() ?? [];
        foreach (var r in rows)
        {
            Alerts.Add(new AlertRowViewModel(r.Row, r.Source));
        }
    }

    private void LoadGuests(JsonNode ev)
    {
        Guests.Clear();
        var participants = ev["participants"] as JsonObject;
        foreach (var person in _s.Rules.AttendeesOf(ev))
        {
            Guests.Add(new GuestRow(person, _s.Rules.Initials(person), AnswerOf(participants, person.Email)));
        }
    }

    private static string AnswerOf(JsonObject? participants, string email)
    {
        var p = participants?.Select(x => x.Value).FirstOrDefault(v => string.Equals(CalendarJson.Str(v, "email"), email, StringComparison.OrdinalIgnoreCase));
        return CalendarJson.Str(p, "participationStatus") switch
        {
            "accepted" => "Accepted",
            "declined" => "Declined",
            "tentative" => "Maybe",
            "needs-action" => "Awaiting reply",
            _ => string.Empty,
        };
    }

    private static string LocationOf(JsonNode ev)
    {
        if (ev["locations"] is JsonObject locs)
        {
            foreach (var (_, v) in locs)
            {
                if (CalendarJson.Str(v, "name") is { Length: > 0 } name)
                {
                    return name;
                }
            }
        }

        return ev["virtualLocations"] is JsonObject vls ? vls.Select(p => CalendarJson.Str(p.Value, "uri")).FirstOrDefault(u => u.Length > 0) ?? string.Empty : string.Empty;
    }

    /// <summary>The form as the save builds it from the editor's fields.</summary>
    public EventForm BuildForm() => new()
    {
        Title = _title,
        Description = _description,
        Location = _location,
        Start = StartDateTime,
        End = EndDateTime,
        AllDay = _allDay,
        TimeZone = _allDay ? null : _timeZone,
        Calendar = _calendarId,
        Repeat = _repeat,
        Custom = _repeat == 5 ? Recurrence.Rule : null,
        UseDefaultAlerts = _useDefaultAlerts,
        DefaultAlerts = _s.Rules.CalendarDefaults(CalendarNode(), _allDay),
        Rows = [.. Alerts.Select(a => a.ToEditRow())],
        Kept = _keptAlerts,
        ReservedAlertIds = _reservedAlerts,
        Attendees = [.. Guests.Select(g => g.Person)],
        Send = _notify && (Guests.Count > 0 || _event is not null),
        Organizer = UserOrganizes,
        FreeBusy = _freeBusy,
        Privacy = _privacy,
        Me = _s.Source.Mine,
    };

    private CalendarEventFormView FormView(string? virtualLocation) => new(
        _title.Trim(),
        _description.Trim(),
        CalendarJson.FormatLocal(StartDateTime),
        CalendarJson.FormatLocal(_allDay ? _endDate.ToDateTime(new TimeOnly(23, 59, 59)) : EndDateTime),
        _allDay,
        _location,
        virtualLocation ?? (MeetingLink ?? string.Empty),
        _calendarId,
        _event is null ? null : CalendarJson.Str(_event, "uid"),
        [.. Guests.Select(g => g.Person.Email)]);

    private string? Validate()
    {
        if (_calendarId.Length == 0)
        {
            return "Choose a calendar.";
        }

        if (!_allDay && (ParseTime(_startTime) is null || ParseTime(_endTime) is null))
        {
            return "Use HH:MM for the time.";
        }

        return !_allDay && EndDateTime < StartDateTime ? "The event ends before it starts." : _allDay && _endDate < _startDate ? "The event ends before it starts." : null;
    }

    /// <summary>Saves: the scope question for a series, the plugin hook, <c>EventEditor.BuildData</c>, then the writer. True when saved and closed; false (Error set, editor stays) when it failed or the user cancelled.</summary>
    public async Task<bool> SaveAsync(CancellationToken ct)
    {
        if (_saving || _readOnly)
        {
            return false;
        }

        if (Validate() is { } problem)
        {
            Error = problem;
            return false;
        }

        if (_s.Source.AccountVersion != _accountVersion)
        {
            Error = "The active account changed. Reopen the event before saving.";
            return false;
        }

        if (_s.Source.AccountKey is not { } account)
        {
            Error = "No account is signed in.";
            return false;
        }

        var scope = EditScope.WholeEvent;
        if (_event is not null && IsSeries && _occurrence is not null && _recurrenceId is not null)
        {
            var asked = await _s.Questions.AskScopeAsync(_title.Length > 0 ? _title : "(no title)", false, Guests.Count > 0, ct).ConfigureAwait(true);
            if (asked is not { } choice)
            {
                return false;
            }

            scope = choice.Scope;
        }
        else if (_event is not null && IsSeries)
        {
            scope = EditScope.All;
        }

        _saving = true;
        OnPropertyChanged(nameof(IsSaving));
        Error = string.Empty;
        try
        {
            return await WriteAsync(account, scope, ct).ConfigureAwait(true);
        }
        finally
        {
            _saving = false;
            OnPropertyChanged(nameof(IsSaving));
        }
    }

    private async Task<bool> WriteAsync(string account, EditScope scope, CancellationToken ct)
    {
        var patch = _s.Hooks is { } hooks && hooks.HasHandlers(HookNames.OnCalendarEventFormSave)
            ? await hooks.TransformAsync(HookNames.OnCalendarEventFormSave, new CalendarEventFormSavePatch(), ct).ConfigureAwait(true)
            : null;
        if (_s.Source.AccountVersion != _accountVersion || !string.Equals(account, _s.Source.AccountKey, StringComparison.Ordinal))
        {
            Error = "The active account changed. Reopen the event before saving.";
            return false;
        }

        var form = BuildForm();
        if (_event is null && form.Attendees.Count > 0 && _s.ParticipantIdentities is { } identities)
        {
            var identityResult = await identities.ListAsync(account, ct).ConfigureAwait(true);
            if (_s.Source.AccountVersion != _accountVersion || !string.Equals(account, _s.Source.AccountKey, StringComparison.Ordinal))
            {
                Error = "The active account changed. Reopen the event before saving.";
                return false;
            }

            if (!identityResult.IsOk && identityResult.Error is not MethodError { Type: "unknownMethod" })
            {
                Error = identityResult.Error!.Message;
                return false;
            }

            var list = identityResult.Value;
            var chosen = list?.FirstOrDefault(i => i.IsDefault) ?? (list is { Count: > 0 } ? list[0] : null);
            if (chosen is not null)
            {
                var address = chosen.CalendarAddress.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ? chosen.CalendarAddress[7..] : chosen.CalendarAddress;
                if (!string.IsNullOrWhiteSpace(address))
                {
                    form = form with { PreferredOrganizer = new CalendarPerson(chosen.Name, address), Me = [address, .. form.Me.Where(m => !string.Equals(m, address, StringComparison.OrdinalIgnoreCase))] };
                }
            }
        }

        var built = _s.Rules.BuildData(form, _event, _occurrence);
        var data = built.Data;
        if (patch?.VirtualLocation is { } vl)
        {
            data["virtualLocations"] = vl.Trim().Length > 0
                ? new JsonObject { ["vl1"] = new JsonObject { ["@type"] = "VirtualLocation", ["name"] = null, ["description"] = null, ["uri"] = vl.Trim() } }
                : null;
        }

        var write = new EventWrite(account, _event is null ? null : CalendarJson.Id(_event), data, scope, scope is EditScope.Occurrence or EditScope.Following ? _recurrenceId : null, built.SendSchedulingMessages && _notify);
        var result = await _s.Writer.SaveEventAsync(write, ct).ConfigureAwait(true);
        if (!result.IsOk)
        {
            Error = result.Error!.Message;
            return false;
        }

        _closed.TrySetResult();
        return true;
    }

    /// <summary>Esc, the close button or a click on the scrim: asks to discard when something changed, then closes.</summary>
    public async Task<bool> RequestCloseAsync(CancellationToken ct)
    {
        if (_saving)
        {
            return false;
        }

        if (!_readOnly && IsDirty)
        {
            var dialog = new ComposerDialog("discard", "Discard changes?", "The event has changes that are not saved.", [new DialogChoice("discard", "Discard", DialogTone.Danger), new DialogChoice("keep", "Keep editing", DialogTone.Primary)]);
            if (!string.Equals(await _s.Questions.AskAsync(dialog, ct).ConfigureAwait(true), "discard", StringComparison.Ordinal))
            {
                return false;
            }
        }

        _closed.TrySetResult();
        return true;
    }

    /// <summary>Closes without a question (after a delete or a duplicate).</summary>
    public void Close() => _closed.TrySetResult();

    /// <summary>Delete (the footer, the event menu): the scope and notify question for a series, a confirmation for a single event.</summary>
    public async Task<bool> DeleteAsync(CancellationToken ct)
    {
        if (_event is null || _s.Source.AccountKey is not { } account)
        {
            return false;
        }

        var title = _title.Length > 0 ? _title : "(no title)";
        var guests = Guests.Count > 0 && UserOrganizes;
        var scope = EditScope.WholeEvent;
        var notify = false;
        if (IsSeries)
        {
            if (await _s.Questions.AskScopeAsync(title, true, guests, ct).ConfigureAwait(true) is not { } asked)
            {
                return false;
            }

            (scope, notify) = (asked.Scope, asked.Notify);
        }
        else
        {
            var choices = guests
                ? new[] { new DialogChoice("notify", "Delete and notify guests", DialogTone.Danger), new DialogChoice("quiet", "Delete without notifying", DialogTone.Danger), new DialogChoice("cancel", "Cancel") }
                : [new DialogChoice("quiet", "Delete", DialogTone.Danger), new DialogChoice("cancel", "Cancel")];
            var answer = await _s.Questions.AskAsync(new ComposerDialog("delete", "Delete event?", $"\"{title}\" will be deleted.", choices), ct).ConfigureAwait(true);
            if (answer is not ("notify" or "quiet"))
            {
                return false;
            }

            notify = string.Equals(answer, "notify", StringComparison.Ordinal);
        }

        var result = await _s.Writer.DeleteEventAsync(account, CalendarJson.Id(_event), scope, scope is EditScope.Occurrence or EditScope.Following ? _recurrenceId : null, notify, ct).ConfigureAwait(true);
        if (!result.IsOk)
        {
            Error = result.Error!.Message;
            _s.Toasts?.Show(new ToastRequest("Event not deleted: " + result.Error.Message, true));
            return false;
        }

        _closed.TrySetResult();
        return true;
    }

    /// <summary>The event as a copy: a new event with the same fields (the edit footer's Duplicate, B-CAL-051).</summary>
    public EventEditorViewModel DuplicateCopy()
    {
        var copy = new EventEditorViewModel(_s);
        copy.OpenNew(new NewEventRequest(StartDateTime, EndDateTime, _allDay, _calendarId, _title));
        copy._location = _location;
        copy._description = _description;
        copy._timeZone = _timeZone;
        copy._repeat = _repeat;
        copy._freeBusy = _freeBusy;
        copy._privacy = _privacy;
        copy._useDefaultAlerts = _useDefaultAlerts;
        copy._customLoaded = _customLoaded;
        if (_repeat == 5)
        {
            copy.Recurrence.Load(Recurrence.Rule, StartDateTime);
        }

        foreach (var a in Alerts)
        {
            copy.Alerts.Add(new AlertRowViewModel(a.Row));
        }

        foreach (var g in Guests)
        {
            copy.Guests.Add(new GuestRow(g.Person, g.Initials, string.Empty));
        }

        return copy;
    }
}
