// Mirrors: Rust cal_editor.rs guests (:131 build_participant_map, availability :1331-:1430, alerts :791 alerts_customize), Bulwark participant-input.tsx (chips, contact suggestions, Enter or comma commits),
// participant-availability.tsx, find-a-time.tsx (FindATime.MaxSuggestions 5, only with the availability capability, D-M4-13), alert-list-editor.tsx; B-CAL-013, 034, 035; I-337, I-338.

using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using Barbican.Core.Calendar.Editing;
using Barbican.Core.Calendar.Reminders;

namespace Barbican.Presentation.Calendar.Editors;

public sealed partial class EventEditorViewModel
{
    private string _guestInput = string.Empty;
    private IReadOnlyList<GuestSuggestion> _suggestions = [];
    private IReadOnlyList<JsonNode>? _cards;
    private IReadOnlyList<FreeSlot> _slots = [];
    private string _slotsStatus = string.Empty;
    private int _availabilityVersion;

    public ObservableCollection<GuestRow> Guests { get; } = [];

    public ObservableCollection<AlertRowViewModel> Alerts { get; } = [];

    public IReadOnlyList<GuestSuggestion> Suggestions
    {
        get => _suggestions;
        private set => SetProperty(ref _suggestions, value);
    }

    /// <summary>The "Find a time" button and the per-guest availability exist only when the login's server offers availability.</summary>
    public bool AvailabilitySupported => _s.Availability is { } a && _s.Source.AccountKey is { } k && a.IsSupported(k);

    /// <summary>At most five suggested times (<c>FindATime.MaxSuggestions</c>).</summary>
    public IReadOnlyList<FreeSlot> Slots
    {
        get => _slots;
        private set => SetProperty(ref _slots, value);
    }

    public string SlotsStatus
    {
        get => _slotsStatus;
        private set => SetProperty(ref _slotsStatus, value);
    }

    /// <summary>Use the calendar's default alerts (the rows are not sent).</summary>
    public bool UseDefaultAlerts
    {
        get => _useDefaultAlerts;
        set
        {
            if (SetProperty(ref _useDefaultAlerts, value))
            {
                OnPropertyChanged(nameof(CustomAlerts));
            }
        }
    }

    public bool CustomAlerts => !_useDefaultAlerts;

    /// <summary>"10 minutes before" or "None": the calendar's default alerts for this kind of event.</summary>
    public string DefaultAlertsLabel => _s.Rules.AlertsLabel(_s.Rules.CalendarDefaults(CalendarNode(), AllDay));

    /// <summary>The text of the guest field.</summary>
    public string GuestInput
    {
        get => _guestInput;
        set
        {
            if (SetProperty(ref _guestInput, value ?? string.Empty))
            {
                _ = UpdateSuggestionsAsync();
            }
        }
    }

    /// <summary>Adds the typed guest (Enter or comma): a name and address, or a bare address. False when it is not an address.</summary>
    public bool CommitGuest()
    {
        var text = _guestInput.Trim().TrimEnd(',').Trim();
        if (text.Length == 0)
        {
            return false;
        }

        if (_s.Rules.ParsePerson(text) is not { } person)
        {
            Error = "\"" + text + "\" is not an email address.";
            return false;
        }

        Error = string.Empty;
        AddGuest(person);
        _guestInput = string.Empty;
        Suggestions = [];
        OnPropertyChanged(nameof(GuestInput));
        return true;
    }

    public void AddGuest(CalendarPerson person)
    {
        ArgumentNullException.ThrowIfNull(person);
        if (Guests.Any(g => string.Equals(g.Person.Email, person.Email, StringComparison.OrdinalIgnoreCase)) || _s.Source.Mine.Contains(person.Email.ToLowerInvariant(), StringComparer.Ordinal))
        {
            return;
        }

        Guests.Add(new GuestRow(person, _s.Rules.Initials(person), string.Empty));
        OnPropertyChanged(nameof(HasGuests));
        RefreshAvailabilityLater();
    }

    /// <summary>Picks a suggestion: a person, or every member of a group.</summary>
    public void PickSuggestion(GuestSuggestion suggestion)
    {
        ArgumentNullException.ThrowIfNull(suggestion);
        foreach (var p in suggestion.People)
        {
            AddGuest(p);
        }

        _guestInput = string.Empty;
        Suggestions = [];
        OnPropertyChanged(nameof(GuestInput));
    }

    public void RemoveGuest(GuestRow guest)
    {
        if (Guests.Remove(guest))
        {
            OnPropertyChanged(nameof(HasGuests));
        }
    }

    public bool HasGuests => Guests.Count > 0;

    /// <summary>Adds a reminder row: ten minutes before as a notification.</summary>
    public void AddAlert()
    {
        UseDefaultAlerts = false;
        Alerts.Add(new AlertRowViewModel(new ReminderRow(10, 1, 0)));
    }

    public void RemoveAlert(AlertRowViewModel row) => Alerts.Remove(row);

    /// <summary>"Customise": copies the calendar's default alerts into the rows and stops using the defaults (Rust alerts_customize).</summary>
    public void CustomizeAlerts()
    {
        var (rows, _) = _s.Rules.AlertRows(_s.Rules.CalendarDefaults(CalendarNode(), AllDay));
        Alerts.Clear();
        foreach (var r in rows)
        {
            Alerts.Add(new AlertRowViewModel(r.Row));
        }

        UseDefaultAlerts = false;
    }

    /// <summary>Back to the calendar's defaults.</summary>
    public void UseDefaults()
    {
        Alerts.Clear();
        UseDefaultAlerts = true;
    }

    private JsonNode? CalendarNode() => _s.Source.Calendars.FirstOrDefault(c => string.Equals(CalendarJson.Id(c), _calendarId, StringComparison.Ordinal));

    private async Task UpdateSuggestionsAsync()
    {
        var query = _guestInput.Trim();
        if (query.Length == 0 || _s.Contacts is null || _s.Source.AccountKey is not { } key)
        {
            Suggestions = [];
            return;
        }

        if (_cards is null)
        {
            var read = await _s.Contacts.CardsAsync(key, CancellationToken.None).ConfigureAwait(true);
            _cards = read.IsOk ? read.Value! : [];
        }

        if (!string.Equals(query, _guestInput.Trim(), StringComparison.Ordinal))
        {
            return;
        }

        var have = Guests.Select(g => g.Person.Email).Concat(_s.Source.Mine).ToList();
        Suggestions = [.. _s.Rules.Matching(_cards, query, have, 6).Select(m => new GuestSuggestion(
            m.Members.Count > 0 ? (m.Person.Name.Length > 0 ? m.Person.Name : m.Person.Email) + $" ({m.Members.Count})" : m.Person.Name.Length > 0 ? m.Person.Name : m.Person.Email,
            m.Members.Count > 0 ? string.Join(", ", m.Members.Select(p => p.Email)) : m.Person.Email,
            m.Members.Count > 0 ? m.Members : [m.Person]))];
    }

    private void RefreshAvailabilityLater()
    {
        if (Guests.Count > 0 && AvailabilitySupported)
        {
            _ = RefreshAvailabilityAsync(CancellationToken.None);
        }
    }

    /// <summary>Reads each guest's free/busy for the event's window and labels the chips.</summary>
    public async Task RefreshAvailabilityAsync(CancellationToken ct)
    {
        if (_s.Availability is not { } service || _s.Source.AccountKey is not { } key || Guests.Count == 0)
        {
            return;
        }

        var version = ++_availabilityVersion;
        var (from, to) = Window();
        var result = await service.BusyAsync(key, [.. Guests.Select(g => g.Person.Email.ToLowerInvariant())], from, to, ct).ConfigureAwait(true);
        if (version != _availabilityVersion)
        {
            return;
        }

        foreach (var g in Guests)
        {
            if (!result.IsOk || !result.Value!.TryGetValue(g.Person.Email.ToLowerInvariant(), out var periods))
            {
                g.Availability = "Unknown";
                g.AvailabilityRank = 0;
            }
            else if (!periods.IsOk)
            {
                g.Availability = "Unknown";
                g.AvailabilityRank = 0;
            }
            else
            {
                var status = _s.Rules.AvailabilityFor(periods.Value!, from, to);
                g.Availability = status.Label;
                g.AvailabilityRank = status.Rank;
            }
        }
    }

    /// <summary>"Find a time": up to five slots as long as the event, when every guest is free (Principal/getAvailability for the next working days).</summary>
    public async Task FindATimeAsync(CancellationToken ct)
    {
        if (!AvailabilitySupported || _s.Availability is not { } service || _s.Source.AccountKey is not { } key || Guests.Count == 0)
        {
            return;
        }

        SlotsStatus = "Looking for a time…";
        var clock = _s.Time ?? TimeProvider.System;
        var now = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Barbican.Core.Settings.Pages.SettingDisplayTimeZone.Resolve(_s.DefaultZone, clock.LocalTimeZone)).DateTime;
        var from = _s.Rules.ZoneToUtcMs(now, null);
        var to = from + (21L * 24 * 3600 * 1000);
        var read = await service.BusyAsync(key, [.. Guests.Select(g => g.Person.Email.ToLowerInvariant())], from, to, ct).ConfigureAwait(true);
        if (!read.IsOk)
        {
            Slots = [];
            SlotsStatus = "Availability could not be read.";
            return;
        }

        var busy = read.Value!.Values.Where(r => r.IsOk).SelectMany(r => r.Value!).Select(p => (p.Start, p.End)).ToList();
        var length = (long)Math.Max(15, (EndDateTime - StartDateTime).TotalMinutes) * 60_000;
        var slots = _s.Rules.FreeSlots(now, length, busy, FindATime.MaxSuggestions);
        Slots = [.. slots.Take(FindATime.MaxSuggestions)];
        SlotsStatus = Slots.Count == 0 ? "No common free time in the next working days." : string.Empty;
    }

    /// <summary>Takes a suggested slot as the event's time.</summary>
    public void ApplySlot(FreeSlot slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        var length = EndDateTime - StartDateTime;
        _startDate = DateOnly.FromDateTime(slot.Start);
        _endDate = DateOnly.FromDateTime(slot.End);
        _startTime = slot.Start.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        _endTime = slot.End.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        _ = length;
        OnPropertyChanged(nameof(StartDate));
        OnPropertyChanged(nameof(EndDate));
        OnPropertyChanged(nameof(StartTime));
        OnPropertyChanged(nameof(EndTime));
        Slots = [];
        RefreshAvailabilityLater();
    }

    private (long From, long To) Window()
    {
        var start = StartDateTime;
        var end = AllDay ? EndDateTime.AddDays(1) : EndDateTime;
        if (end <= start)
        {
            end = start.AddHours(1);
        }

        return (_s.Rules.ZoneToUtcMs(start, _timeZone), _s.Rules.ZoneToUtcMs(end, _timeZone));
    }
}
