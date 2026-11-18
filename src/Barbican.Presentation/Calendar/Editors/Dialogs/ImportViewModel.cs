// Mirrors: Rust cal_extra.rs import (:506), Bulwark components/calendar/ical-import-modal.tsx (file at most 5 MB, a list of the events found with select all, a target calendar, the result count) and G-004 (the dialog asks whether to keep the file's alerts;
// the default keeps them, as Bulwark's import does); I-340. The import card.

using System.Globalization;
using System.Text.Json.Nodes;
using Barbican.Core.Calendar.Editing;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Barbican.Presentation.Calendar.Editors.Dialogs;

/// <summary>Import events from an .ics file into a calendar.</summary>
public sealed class ImportViewModel : ObservableObject, ICloseableCard
{
    /// <summary>The largest file read: 5 MB.</summary>
    public const int MaxBytes = 5 * 1024 * 1024;

    private readonly IEditorSource _source;
    private readonly IIcsImporter _importer;
    private readonly ICalendarRules _rules;
    private readonly ICalendarFilePicker _picker;
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private IReadOnlyList<JsonNode> _events = [];
    private string _calendarId;
    private bool _keepAlerts = true;
    private string _error = string.Empty;
    private string _status = string.Empty;
    private string _fileName = string.Empty;
    private bool _busy;

    public ImportViewModel(IEditorSource source, IIcsImporter importer, ICalendarRules rules, ICalendarFilePicker picker)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(importer);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(picker);
        _source = source;
        _importer = importer;
        _rules = rules;
        _picker = picker;
        var subs = new HashSet<string>(source.Subscriptions, StringComparer.Ordinal);
        Calendars = [.. source.Calendars
            .Where(c => CalendarJson.Writable(c, subs) && !CalendarJson.IsShared(CalendarJson.Id(c)))
            .Select(c => new CalendarChoice(CalendarJson.Id(c), CalendarJson.Str(c, "name"), CalendarJson.Str(c, "color")))];
        var def = source.Calendars.FirstOrDefault(c => CalendarJson.Flag(c, "isDefault"));
        _calendarId = def is not null && Calendars.Any(c => string.Equals(c.Id, CalendarJson.Id(def), StringComparison.Ordinal)) ? CalendarJson.Id(def) : Calendars.Count > 0 ? Calendars[0].Id : string.Empty;
    }

#pragma warning disable VSTHRD003 // completed by the card itself
    public Task Closed => _closed.Task;
#pragma warning restore VSTHRD003

    public IReadOnlyList<CalendarChoice> Calendars { get; }

    /// <summary>The events found, each with its checkbox.</summary>
    public IReadOnlyList<ImportItem> Items { get; private set; } = [];

    public string FileName
    {
        get => _fileName;
        private set => SetProperty(ref _fileName, value);
    }

    public string CalendarId
    {
        get => _calendarId;
        set => SetProperty(ref _calendarId, value ?? string.Empty);
    }

    /// <summary>Keep the alerts the file carries (the default); off drops them.</summary>
    public bool KeepAlerts
    {
        get => _keepAlerts;
        set => SetProperty(ref _keepAlerts, value);
    }

    public string Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    /// <summary>The result line ("Imported 3 events.").</summary>
    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public bool IsBusy
    {
        get => _busy;
        private set => SetProperty(ref _busy, value);
    }

    public IReadOnlyList<JsonNode> Selected => [.. Items.Where(i => i.Selected).Select(i => _events[i.Index])];

    /// <summary>The "keep the alerts?" question shows only when the chosen events carry alerts.</summary>
    public bool ShowAlertsPrompt => Items.Count > 0 && _rules.HasAlerts(Selected);

    public int SelectedCount => Items.Count(i => i.Selected);

    /// <summary>Asks for a file and reads its events. False when cancelled or nothing was found (Error says why).</summary>
    public async Task<bool> PickAsync(CancellationToken ct)
    {
        var file = await _picker.OpenIcsAsync(ct).ConfigureAwait(true);
        return file is not null && await LoadAsync(file, ct).ConfigureAwait(true);
    }

    public async Task<bool> LoadAsync(IcsFile file, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(file);
        Error = string.Empty;
        Status = string.Empty;
        FileName = file.Name;
        if (file.Bytes.Length > MaxBytes)
        {
            Error = "The file is too large (5 MB at most).";
            return false;
        }

        if (!file.Name.EndsWith(".ics", StringComparison.OrdinalIgnoreCase) && !file.Name.EndsWith(".ical", StringComparison.OrdinalIgnoreCase) && !file.Name.EndsWith(".ifb", StringComparison.OrdinalIgnoreCase))
        {
            Error = "Choose an .ics file.";
            return false;
        }

        if (_source.AccountKey is not { } account)
        {
            return false;
        }

        IsBusy = true;
        try
        {
            var read = await _importer.ParseAsync(account, file.Bytes, ct).ConfigureAwait(true);
            if (!read.IsOk)
            {
                Error = read.Error!.Message;
                return false;
            }

            SetEvents(read.Value!);
            if (_events.Count == 0)
            {
                Error = "No events were found in this file.";
                return false;
            }

            return true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SetEvents(IReadOnlyList<JsonNode> events)
    {
        _events = events;
        Items = [.. events.Select((e, i) => new ImportItem(i, CalendarJson.Str(e, "title") is { Length: > 0 } t ? t : "(no title)", WhenOf(e), true))];
        foreach (var item in Items)
        {
            item.PropertyChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(ShowAlertsPrompt));
                OnPropertyChanged(nameof(SelectedCount));
            };
        }

        OnPropertyChanged(nameof(Items));
        OnPropertyChanged(nameof(ShowAlertsPrompt));
        OnPropertyChanged(nameof(SelectedCount));
    }

    public void SelectAll(bool on)
    {
        foreach (var i in Items)
        {
            i.Selected = on;
        }
    }

    /// <summary>Imports the chosen events into the chosen calendar; Status says how many were stored.</summary>
    public async Task<bool> ImportAsync(CancellationToken ct)
    {
        var chosen = Selected;
        if (chosen.Count == 0 || _calendarId.Length == 0 || _source.AccountKey is not { } account)
        {
            Error = chosen.Count == 0 ? "Select at least one event." : "Choose a calendar.";
            return false;
        }

        IsBusy = true;
        try
        {
            var keep = _keepAlerts || !_rules.HasAlerts(chosen);
            var result = await _importer.ImportAsync(account, _calendarId, chosen, keep, ct).ConfigureAwait(true);
            if (!result.IsOk)
            {
                Error = result.Error!.Message;
                return false;
            }

            Error = string.Empty;
            Status = result.Value == 1 ? "Imported 1 event." : $"Imported {result.Value} events.";
            return true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void Close() => _closed.TrySetResult();

    private static string WhenOf(JsonNode e)
    {
        if (CalendarJson.ParseLocal(CalendarJson.Str(e, "start")) is not { } s)
        {
            return string.Empty;
        }

        return s.ToString(CalendarJson.Flag(e, "showWithoutTime") ? "ddd, MMM d yyyy" : "ddd, MMM d yyyy HH:mm", CultureInfo.InvariantCulture);
    }
}
