// Mirrors: Rust cal_extra.rs webcal UI (:506-:860), Bulwark components/calendar/ical-subscription-modal.tsx (address, name, colour, refresh interval 15/30/60/360/1440; edit, refresh, unsubscribe), FeedRules.Check (G-006: LAN and localhost blocked, http warned); B-CAL-041..044; I-307.
// The subscription card: add a feed, or edit, refresh and remove an existing one.

using System.Text.Json.Nodes;
using Barbican.Core.Calendar.Editing;
using Barbican.Core.Calendar.Feeds;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Barbican.Presentation.Calendar.Editors.Dialogs;

/// <summary>Subscribe to a webcal feed, or edit one.</summary>
public sealed class SubscriptionViewModel : ObservableObject, ICloseableCard
{
    private readonly IEditorSource _source;
    private readonly ICalendarSubscriptions _subscriptions;
    private readonly ICalendarAdmin _admin;
    private readonly ICalendarRules _rules;
    private readonly CalendarEditorHost? _host;
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string _url = string.Empty;
    private string _name = string.Empty;
    private string _color = CalendarJson.Colors[0].Hex;
    private int _interval = 2;
    private string _error = string.Empty;
    private string _warning = string.Empty;
    private string _status = string.Empty;
    private bool _busy;

    public SubscriptionViewModel(IEditorSource source, ICalendarSubscriptions subscriptions, ICalendarAdmin admin, ICalendarRules rules, CalendarEditorHost? host = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(subscriptions);
        ArgumentNullException.ThrowIfNull(admin);
        ArgumentNullException.ThrowIfNull(rules);
        _source = source;
        _subscriptions = subscriptions;
        _admin = admin;
        _rules = rules;
        _host = host;
    }

#pragma warning disable VSTHRD003 // completed by the card itself
    public Task Closed => _closed.Task;
#pragma warning restore VSTHRD003

    /// <summary>The refresh intervals offered, in minutes.</summary>
    public static IReadOnlyList<int> Intervals => FeedRules.IntervalsMinutes;

    /// <summary>"Every 15 minutes", "Every hour", "Every 6 hours", "Once a day".</summary>
    public static string IntervalLabel(int minutes) => minutes switch
    {
        15 => "Every 15 minutes",
        30 => "Every 30 minutes",
        60 => "Every hour",
        360 => "Every 6 hours",
        1440 => "Once a day",
        _ => $"Every {minutes} minutes",
    };

    /// <summary>The calendar of the feed being edited; empty for a new subscription.</summary>
    public string CalendarId { get; private set; } = string.Empty;

    public bool IsEdit => CalendarId.Length > 0;

    public string Heading => IsEdit ? "Edit subscription" : "Subscribe to a calendar";

    public string Url
    {
        get => _url;
        set
        {
            if (SetProperty(ref _url, value ?? string.Empty))
            {
                Check();
            }
        }
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value ?? string.Empty);
    }

    public string Color
    {
        get => _color;
        set => SetProperty(ref _color, value ?? string.Empty);
    }

    /// <summary>An index into <see cref="Intervals"/>; 60 minutes by default.</summary>
    public int Interval
    {
        get => _interval;
        set => SetProperty(ref _interval, Math.Clamp(value, 0, Intervals.Count - 1));
    }

    /// <summary>Why the address cannot be used (a local or private address, a login in the address), or empty.</summary>
    public string Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    /// <summary>The http warning ("the feed is not encrypted"), or empty.</summary>
    public string Warning
    {
        get => _warning;
        private set => SetProperty(ref _warning, value);
    }

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

    /// <summary>Starts from an existing subscription (edit).</summary>
    public void Edit(CalendarSubscription existing)
    {
        ArgumentNullException.ThrowIfNull(existing);
        CalendarId = existing.CalendarId;
        var calendar = _source.Calendars.FirstOrDefault(c => string.Equals(CalendarJson.Id(c), existing.CalendarId, StringComparison.Ordinal));
        _name = CalendarJson.Str(calendar, "name");
        _color = CalendarJson.Str(calendar, "color") is { Length: > 0 } col ? col : _color;
        _url = existing.Url;
        _interval = Math.Max(0, Intervals.ToList().IndexOf(existing.IntervalMinutes));
        OnPropertyChanged(string.Empty);
    }

    private void Check()
    {
        Error = string.Empty;
        Warning = string.Empty;
        if (_url.Trim().Length == 0)
        {
            return;
        }

        var verdict = _rules.CheckFeed(_url.Trim());
        if (verdict.Blocked is { } why)
        {
            Error = why;
        }
        else if (verdict.Url is null)
        {
            Error = "Enter a webcal:// or https:// address.";
        }
        else if (verdict.Warning is { } w)
        {
            Warning = w;
        }
    }

    /// <summary>Subscribes (or saves the edit). False when the address is blocked or the write failed.</summary>
    public async Task<bool> SaveAsync(CancellationToken ct)
    {
        Check();
        if (_url.Trim().Length == 0)
        {
            Error = "Enter a webcal:// or https:// address.";
            return false;
        }

        if (Error.Length > 0 || _source.AccountKey is not { } account)
        {
            return false;
        }

        IsBusy = true;
        try
        {
            var sub = new CalendarSubscription(CalendarId, _url.Trim(), Intervals[_interval]);
            var name = _name.Trim().Length > 0 ? _name.Trim() : HostOf(_rules.CheckFeed(_url.Trim()).Url);
            var result = await _subscriptions.SaveAsync(account, sub, name, ct).ConfigureAwait(true);
            if (!result.IsOk)
            {
                Error = result.Error!.Message;
                return false;
            }

            await ApplyColorAsync(account, name, ct).ConfigureAwait(true);
            Close();
            return true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string HostOf(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : "Calendar";

    private async Task ApplyColorAsync(string account, string name, CancellationToken ct)
    {
        var id = CalendarId.Length > 0
            ? CalendarId
            : _source.Calendars.Where(c => string.Equals(CalendarJson.Str(c, "name"), name, StringComparison.Ordinal)).Select(CalendarJson.Id).FirstOrDefault();
        if (id is { Length: > 0 })
        {
            _ = await _admin.UpdateCalendarAsync(account, id, new JsonObject { ["name"] = name, ["color"] = _color }, ct).ConfigureAwait(true);
        }
    }

    /// <summary>Fetches the feed now; the status line says how many events changed.</summary>
    public async Task RefreshAsync(CancellationToken ct)
    {
        if (!IsEdit || _source.AccountKey is not { } account)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _subscriptions.RefreshAsync(account, CalendarId, ct).ConfigureAwait(true);
            Status = result.IsOk ? (result.Value == 0 ? "Already up to date." : $"{result.Value} events changed.") : result.Error!.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Unsubscribe: asks, then removes the feed with its calendar.</summary>
    public async Task<bool> UnsubscribeAsync(CancellationToken ct)
    {
        if (!IsEdit || _source.AccountKey is not { } account)
        {
            return false;
        }

        if (_host is not null && !await _host.ConfirmAsync("unsubscribe", "Unsubscribe?", $"\"{_name}\" and its events will be removed from your calendars.", "Unsubscribe", true, ct).ConfigureAwait(true))
        {
            return false;
        }

        var result = await _subscriptions.RemoveAsync(account, CalendarId, true, ct).ConfigureAwait(true);
        if (!result.IsOk)
        {
            Error = result.Error!.Message;
            return false;
        }

        Close();
        return true;
    }

    public void Close() => _closed.TrySetResult();
}
