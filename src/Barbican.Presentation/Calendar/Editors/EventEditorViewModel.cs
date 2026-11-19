// Mirrors: Rust cal_editor.rs:512 open_new, :554 open_existing, :210 Form, :1037 save, :1177 delete; Bulwark components/calendar/event-modal.tsx (fields :1149-1384 view mode, :1442-1473 date and time pickers, :1807-1826 footer), quick-create-popover.tsx;
// B-CAL-011..017, 051; I-332..I-339. The event editor as an in-pane card: every field as plain values, the dirty check, the form a save is built from. No UI types.

using System.Globalization;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Barbican.Presentation.Calendar.Editors;

/// <summary>The event editor: a new event, an existing one (or one occurrence of a series), or the read-only view of an event the user cannot edit.</summary>
public sealed partial class EventEditorViewModel : ObservableObject
{
    /// <summary>The repeat choices in index order; 5 is the custom rule.</summary>
    public static IReadOnlyList<string> RepeatChoices { get; } = ["Does not repeat", "Daily", "Weekly", "Monthly", "Yearly", "Custom…"];

    public static IReadOnlyList<string> ShowAsChoices { get; } = ["Busy", "Free"];

    public static IReadOnlyList<string> VisibilityChoices { get; } = ["Public", "Private", "Secret"];

    private readonly EventEditorServices _s;
    private readonly long _accountVersion;
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private JsonNode? _event;
    private (DateTime Start, DateTime End)? _occurrence;
    private string? _recurrenceId;
    private string _baseline = string.Empty;
    private string _title = string.Empty;
    private string _location = string.Empty;
    private string _description = string.Empty;
    private bool _allDay;
    private DateOnly _startDate;
    private DateOnly _endDate;
    private string _startTime = "09:00";
    private string _endTime = "10:00";
    private string? _timeZone;
    private bool _zoneExplicit;
    private string _calendarId = string.Empty;
    private int _repeat;
    private int _freeBusy;
    private int _privacy;
    private string _error = string.Empty;
    private bool _saving;
    private bool _readOnly;
    private bool _useDefaultAlerts = true;
    private bool _notify = true;

    public EventEditorViewModel(EventEditorServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _s = services;
        _accountVersion = services.Source.AccountVersion;
        Recurrence = new RecurrenceEditorViewModel(services.Rules);
        Recurrence.PropertyChanged += (_, _) => OnPropertyChanged(nameof(RepeatSummary));
        var clock = services.Time ?? TimeProvider.System;
        var now = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Barbican.Core.Settings.Pages.SettingDisplayTimeZone.Resolve(services.DefaultZone, clock.LocalTimeZone)).DateTime;
        _startDate = _endDate = DateOnly.FromDateTime(now);
    }

    /// <summary>Completes when the editor closed (saved, discarded or deleted).</summary>
#pragma warning disable VSTHRD003 // the source is completed by the editor itself
    public Task Closed => _closed.Task;
#pragma warning restore VSTHRD003

    public RecurrenceEditorViewModel Recurrence { get; }

    public IReadOnlyList<CalendarChoice> Calendars { get; private set; } = [];

    /// <summary>The event being edited (null for a new one).</summary>
    public JsonNode? Event => _event;

    public bool IsNew => _event is null;

    /// <summary>The event is part of a series.</summary>
    public bool IsSeries => _event is not null && _event["recurrenceRules"] is JsonArray { Count: > 0 };

    public bool TwelveHour => _s.TwelveHour;

    /// <summary>"New event", "Edit event" or "Event" (view mode).</summary>
    public string Heading => _readOnly ? "Event" : IsNew ? "New event" : "Edit event";

    /// <summary>View mode: the user cannot edit this event (a guest of someone else's event, a read-only calendar, a feed).</summary>
    public bool ReadOnly
    {
        get => _readOnly;
        private set
        {
            if (SetProperty(ref _readOnly, value))
            {
                OnPropertyChanged(nameof(Heading));
            }
        }
    }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value ?? string.Empty);
    }

    public string Location
    {
        get => _location;
        set
        {
            if (SetProperty(ref _location, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(MeetingLink));
            }
        }
    }

    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value ?? string.Empty);
    }

    public bool AllDay
    {
        get => _allDay;
        set
        {
            if (SetProperty(ref _allDay, value))
            {
                RefreshAvailabilityLater();
            }
        }
    }

    public DateOnly StartDate
    {
        get => _startDate;
        set
        {
            if (value == _startDate || !SetProperty(ref _startDate, value))
            {
                return;
            }

            // moving the start moves the end by the same amount, as every calendar does
            if (_endDate < _startDate)
            {
                EndDate = _startDate;
            }

            RefreshAvailabilityLater();
        }
    }

    public DateOnly EndDate
    {
        get => _endDate;
        set
        {
            if (SetProperty(ref _endDate, value))
            {
                RefreshAvailabilityLater();
            }
        }
    }

    /// <summary>"HH:mm" (24 hour); the view shows it in the user's clock format.</summary>
    public string StartTime
    {
        get => _startTime;
        set
        {
            if (SetProperty(ref _startTime, value ?? string.Empty))
            {
                RefreshAvailabilityLater();
            }
        }
    }

    public string EndTime
    {
        get => _endTime;
        set
        {
            if (SetProperty(ref _endTime, value ?? string.Empty))
            {
                RefreshAvailabilityLater();
            }
        }
    }

    /// <summary>IANA zone of the times; null is floating.</summary>
    public string? TimeZone
    {
        get => _timeZone;
        set
        {
            _zoneExplicit = true;
            if (SetProperty(ref _timeZone, value))
            {
                OnPropertyChanged(nameof(ZoneLabel));
            }
        }
    }

    public string ZoneLabel => _timeZone ?? "Floating";

    public string CalendarId
    {
        get => _calendarId;
        set
        {
            if (SetProperty(ref _calendarId, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(DefaultAlertsLabel));
                OnPropertyChanged(nameof(CalendarColor));
            }
        }
    }

    public string CalendarColor => Calendars.FirstOrDefault(c => string.Equals(c.Id, _calendarId, StringComparison.Ordinal))?.Color ?? "#3b82f6";

    /// <summary>0 none, 1 daily, 2 weekly, 3 monthly, 4 yearly, 5 custom.</summary>
    public int Repeat
    {
        get => _repeat;
        set
        {
            var v = Math.Clamp(value, 0, 5);
            if (!SetProperty(ref _repeat, v))
            {
                return;
            }

            if (v == 5 && !_customLoaded)
            {
                Recurrence.Load(null, StartDateTime);
                _customLoaded = true;
            }

            OnPropertyChanged(nameof(RepeatSummary));
            OnPropertyChanged(nameof(CustomOpen));
        }
    }

    private bool _customLoaded;

    /// <summary>The custom recurrence editor shows while Repeat is Custom.</summary>
    public bool CustomOpen => _repeat == 5;

    public string RepeatSummary => _repeat == 5 ? Recurrence.Summary : RepeatChoices[_repeat];

    /// <summary>0 busy, 1 free.</summary>
    public int FreeBusy
    {
        get => _freeBusy;
        set => SetProperty(ref _freeBusy, Math.Clamp(value, 0, 1));
    }

    /// <summary>0 public, 1 private, 2 secret.</summary>
    public int Privacy
    {
        get => _privacy;
        set => SetProperty(ref _privacy, Math.Clamp(value, 0, 2));
    }

    /// <summary>Send invitations and updates to the guests (only offered to the organiser, with guests).</summary>
    public bool Notify
    {
        get => _notify;
        set => SetProperty(ref _notify, value);
    }

    public string Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    public bool IsSaving => _saving;

    /// <summary>The link to join, when the location or the virtual location is a web address.</summary>
    public string? MeetingLink => MeetingLinks.Find(_location, _event);

    public bool UserOrganizes => _event is null || _s.Rules.UserOrganizes(_event, _s.Source.Mine);

    /// <summary>The organiser line of an event someone else organises ("Organised by Ann Lee"), else empty.</summary>
    public string OrganizerLine
    {
        get
        {
            if (_event is null || UserOrganizes || _s.Rules.Organizer(_event) is not { } o)
            {
                return string.Empty;
            }

            return "Organised by " + (o.Name.Length > 0 ? o.Name : o.Email);
        }
    }

    internal DateTime StartDateTime => _startDate.ToDateTime(AllDay ? TimeOnly.MinValue : ParseTime(_startTime) ?? TimeOnly.MinValue);

    internal DateTime EndDateTime => _endDate.ToDateTime(AllDay ? TimeOnly.MinValue : ParseTime(_endTime) ?? TimeOnly.MinValue);

    internal static TimeOnly? ParseTime(string text) =>
        TimeOnly.TryParseExact(text.Trim(), ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : null;

    /// <summary>The choices of the calendar field: the calendars the user may add events to.</summary>
    internal void LoadCalendars(string? preferred)
    {
        var writable = _s.Source.Calendars.Where(c => CalendarJson.Writable(c, new HashSet<string>(_s.Source.Subscriptions, StringComparer.Ordinal))).ToList();
        Calendars = [.. writable.Select(c => new CalendarChoice(CalendarJson.Id(c), CalendarJson.Str(c, "name") is { Length: > 0 } n ? n : "Calendar", CalendarJson.Str(c, "color") is { Length: > 0 } col ? col : "#3b82f6"))];
        var def = writable.FirstOrDefault(c => CalendarJson.Flag(c, "isDefault") && !CalendarJson.IsShared(CalendarJson.Id(c))) ?? writable.FirstOrDefault();
        _calendarId = preferred is { Length: > 0 } && Calendars.Any(c => string.Equals(c.Id, preferred, StringComparison.Ordinal))
            ? preferred
            : def is null ? string.Empty : CalendarJson.Id(def);
        OnPropertyChanged(nameof(Calendars));
        OnPropertyChanged(nameof(CalendarId));
        OnPropertyChanged(nameof(CalendarColor));
    }

    /// <summary>The state of the editor as one string, for the dirty check (Bulwark event-modal compares with the opening state).</summary>
    public string Fingerprint() => string.Join(
        '\u001f',
        _title, _location, _description, _allDay, _startDate, _endDate, _startTime, _endTime, _timeZone, _calendarId, _repeat, _repeat == 5 ? Recurrence.Summary : string.Empty,
        _freeBusy, _privacy, _useDefaultAlerts, string.Join(',', Alerts.Select(a => $"{a.Value}:{a.Unit}:{a.Action}")), string.Join(',', Guests.Select(g => g.Person.Email)));

    /// <summary>Anything changed since the editor opened.</summary>
    public bool IsDirty => !string.Equals(_baseline, Fingerprint(), StringComparison.Ordinal);

    private void Rebase() => _baseline = Fingerprint();
}
