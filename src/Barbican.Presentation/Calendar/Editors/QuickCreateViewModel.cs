// Mirrors: Rust cal_editor.rs:1431 quick, :1456 qc_save, :1477 qc_more; Bulwark components/calendar/quick-create-popover.tsx (title, the day and time, the calendar, "More options", Save; an empty title saves as "No title"; Esc and a click outside close); B-CAL-014; I-324.
// The quick-create popover as a small card. Bulwark's popover has no natural-language start (only the task quick add does), so neither does this one (decision D-M4-17).

using System.Globalization;
using System.Text.Json.Nodes;
using Barbican.Core.Calendar.Editing;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Barbican.Presentation.Calendar.Editors;

/// <summary>The quick-create popover of a clicked slot, day or dragged range.</summary>
public sealed class QuickCreateViewModel : ObservableObject, ICloseableCard
{
    private readonly ICalendarSource _source;
    private readonly ICalendarWriter _writer;
    private readonly NewEventRequest _request;
    private readonly bool _twelveHour;
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string _title;
    private string _calendarId = string.Empty;
    private string _error = string.Empty;

    public QuickCreateViewModel(ICalendarSource source, ICalendarWriter writer, NewEventRequest request, bool twelveHour = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(request);
        _source = source;
        _writer = writer;
        _request = request;
        _twelveHour = twelveHour;
        _title = request.Title;
        var subs = new HashSet<string>(source.Subscriptions, StringComparer.Ordinal);
        var usable = source.Calendars.Where(c => CalendarJson.Writable(c, subs)).ToList();
        Calendars = [.. usable.Select(c => new CalendarChoice(CalendarJson.Id(c), CalendarJson.Str(c, "name") is { Length: > 0 } n ? n : "Calendar", CalendarJson.Str(c, "color") is { Length: > 0 } col ? col : "#3b82f6"))];
        var preferred = request.CalendarId is { Length: > 0 } p && Calendars.Any(c => string.Equals(c.Id, p, StringComparison.Ordinal)) ? p : null;
        var def = usable.FirstOrDefault(c => CalendarJson.Flag(c, "isDefault") && !CalendarJson.IsShared(CalendarJson.Id(c))) ?? usable.FirstOrDefault();
        _calendarId = preferred ?? (def is null ? string.Empty : CalendarJson.Id(def));
    }

#pragma warning disable VSTHRD003 // the source is completed by the popover itself
    public Task Closed => _closed.Task;
#pragma warning restore VSTHRD003

    public IReadOnlyList<CalendarChoice> Calendars { get; }

    /// <summary>Null when Save ended the popover, else the request "More options" asked the full editor to open with (title and calendar carried over).</summary>
    public NewEventRequest? MoreRequest { get; private set; }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value ?? string.Empty);
    }

    public string CalendarId
    {
        get => _calendarId;
        set => SetProperty(ref _calendarId, value ?? string.Empty);
    }

    public string Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    public bool CanSave => _calendarId.Length > 0;

    /// <summary>"Friday, November 20" and "09:00 – 10:00" (or "All day"); a dragged range of days shows both ends.</summary>
    public (string Day, string Time) Label
    {
        get
        {
            var day = _request.Start.ToString("dddd, MMMM d", CultureInfo.InvariantCulture);
            if (_request.AllDay)
            {
                var last = _request.End.Date > _request.Start.Date ? " – " + _request.End.ToString("dddd, MMMM d", CultureInfo.InvariantCulture) : string.Empty;
                return (day + last, "All day");
            }

            var f = _twelveHour ? "h:mm tt" : "HH:mm";
            return (day, _request.Start.ToString(f, CultureInfo.InvariantCulture) + " – " + EndOf().ToString(f, CultureInfo.InvariantCulture));
        }
    }

    private DateTime EndOf() => _request.End > _request.Start ? _request.End : _request.Start.AddHours(1);

    /// <summary>The event the popover creates (Rust qc_save).</summary>
    public JsonObject BuildEvent()
    {
        var allDay = _request.AllDay;
        var days = Math.Max(1, (_request.End.Date - _request.Start.Date).Days + 1);
        var ev = new JsonObject
        {
            ["@type"] = "Event",
            ["uid"] = Guid.NewGuid().ToString("N") + "@barbican",
            ["title"] = _title.Trim(),
            ["start"] = CalendarJson.FormatLocal(allDay ? _request.Start.Date : _request.Start),
            ["duration"] = allDay ? "P" + days.ToString(CultureInfo.InvariantCulture) + "D" : CalendarJson.FormatDuration(EndOf() - _request.Start),
            ["showWithoutTime"] = allDay,
            ["status"] = "confirmed",
            ["freeBusyStatus"] = allDay ? "free" : "busy",
            ["privacy"] = "public",
            ["useDefaultAlerts"] = true,
        };
        if (!allDay)
        {
            ev["timeZone"] = CalendarJson.LocalZoneId();
        }

        return ev;
    }

    /// <summary>Save: the event is created in the chosen calendar and the popover closes. False with <see cref="Error"/> when the write failed.</summary>
    public async Task<bool> SaveAsync(CancellationToken ct)
    {
        if (!CanSave || _source.AccountKey is not { } account)
        {
            return false;
        }

        var result = await _writer.CreateAsync(account, _calendarId, BuildEvent(), ct).ConfigureAwait(true);
        if (!result.IsOk)
        {
            Error = result.Error!.Message;
            return false;
        }

        Close();
        return true;
    }

    /// <summary>"More options": closes the popover and asks for the full editor with the title and calendar.</summary>
    public void More()
    {
        MoreRequest = _request with { Title = _title, CalendarId = _calendarId.Length > 0 ? _calendarId : _request.CalendarId };
        Close();
    }

    public void Close() => _closed.TrySetResult();
}
