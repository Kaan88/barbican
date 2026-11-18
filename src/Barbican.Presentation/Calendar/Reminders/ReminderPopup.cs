// Mirrors: Rust reminders.rs:412 check and :428 show (the stack of reminders in the window, newest first, four at most), :499 act (dismiss, snooze:N, open: switch to the calendar and go to the day), :523 missed_done; Bulwark
// components/calendar/missed-reminders-dialog.tsx, hooks/use-calendar-alerts.ts; I-338, B-CAL-033, B-CAL-035. The view model of the reminder popup and the missed-reminders dialog; cal-ui draws them. Owner cal-data.

using System.ComponentModel;
using Barbican.Core.Calendar;
using Barbican.Core.Calendar.Recurrence;
using Barbican.Core.Calendar.Reminders;
using Barbican.Core.Storage;
using Barbican.Presentation.Calendar.Grid;
using Barbican.Presentation.Common;
using Barbican.Presentation.Shell;

namespace Barbican.Presentation.Calendar.Reminders;

/// <summary>The default <see cref="IReminderPopup"/>.</summary>
public sealed class ReminderPopup : IReminderPopup, IDisposable
{
    private readonly IReminderScheduler _scheduler;
    private readonly ShellViewModel? _shell;
    private readonly ICalendarViewModel? _calendar;
    private readonly IPrefsStore? _prefs;
    private readonly IDispatcher? _dispatcher;
    private readonly TimeProvider _time;
    private readonly Lock _lock = new();
    private IReadOnlyList<DueReminder> _shown = [];
    private IReadOnlyList<DueReminder> _missed = [];

    public ReminderPopup(IReminderScheduler scheduler, ShellViewModel? shell = null, ICalendarViewModel? calendar = null, IPrefsStore? prefs = null, IDispatcher? dispatcher = null, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        _scheduler = scheduler;
        _shell = shell;
        _calendar = calendar;
        _prefs = prefs;
        _dispatcher = dispatcher;
        _time = time ?? TimeProvider.System;
        _scheduler.Due += OnDue;
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <inheritdoc/>
    public IReadOnlyList<DueReminder> Shown
    {
        get
        {
            lock (_lock)
            {
                return _shown;
            }
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<DueReminder> Missed
    {
        get
        {
            lock (_lock)
            {
                return _missed;
            }
        }
    }

    /// <summary>"In 15 minutes", "Now", "Task due" for a shown reminder, in the user's time format.</summary>
    /// <inheritdoc/>
    public string WhenLabel(DueReminder reminder) =>
        ReminderRules.WhenLabel(reminder, _time.GetUtcNow().ToUnixTimeMilliseconds(), _prefs is not null && CalendarSettings.Read(_prefs.Current).TwelveHour, Zone());

    /// <inheritdoc/>
    public async Task DismissAsync(DueReminder reminder, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reminder);
        Remove(reminder.Key, shown: true, missed: true);
        await _scheduler.DismissAsync([reminder], ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task DismissAllAsync(CancellationToken ct)
    {
        var all = Shown;
        lock (_lock)
        {
            _shown = [];
        }

        Notify(nameof(Shown));
        await _scheduler.DismissAsync(all, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public void Snooze(DueReminder reminder, int minutes)
    {
        ArgumentNullException.ThrowIfNull(reminder);
        if (minutes <= 0)
        {
            return;
        }

        Remove(reminder.Key, shown: true, missed: false);
        _scheduler.Snooze([reminder], minutes);
    }

    /// <inheritdoc/>
    public async Task OpenAsync(DueReminder reminder, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reminder);
        Remove(reminder.Key, shown: true, missed: true);
        await _scheduler.DismissAsync([reminder], ct).ConfigureAwait(false);
        Post(() =>
        {
            _ = _shell?.TryActivate(Barbican.Presentation.Shell.AppId.Calendar);
            if (_calendar is null)
            {
                return;
            }

            var at = TimeZones.UtcMsToZone(reminder.StartMs ?? reminder.FireMs, null, Zone());
            _calendar.ShowDay(DateOnly.FromDateTime(at));

            // an occurrence of a series is "id|recurrenceId" as the grid knows it; a task is "task:id"
            _calendar.SelectOccurrence(reminder.Task ? "task:" + reminder.Event : reminder.Event);
        });
    }

    /// <inheritdoc/>
    public async Task LoadMissedAsync(CancellationToken ct)
    {
        var missed = await _scheduler.MissedAsync(ct).ConfigureAwait(false);
        lock (_lock)
        {
            _missed = [.. missed.OrderBy(d => d.StartMs ?? d.FireMs)];
        }

        // the await above resumed on the pool (ConfigureAwait(false)): the views listen on the UI thread
        Post(() => Notify(nameof(Missed)));
    }

    /// <inheritdoc/>
    public async Task CloseMissedAsync(bool open, CancellationToken ct)
    {
        var missed = Missed;
        lock (_lock)
        {
            _missed = [];
        }

        Notify(nameof(Missed));
        await _scheduler.DismissAsync(missed, ct).ConfigureAwait(false);
        if (open)
        {
            Post(() => _ = _shell?.TryActivate(Barbican.Presentation.Shell.AppId.Calendar));
        }
    }

    public void Dispose() => _scheduler.Due -= OnDue;

    // a reminder came due (or was found missed) on the scheduler's thread
    private void OnDue(object? sender, ReminderEventArgs e)
    {
        lock (_lock)
        {
            if (e.Missed)
            {
                _missed = [.. _missed.Concat(e.Reminders.Where(r => !_missed.Any(m => string.Equals(m.Key, r.Key, StringComparison.Ordinal)))).OrderBy(d => d.StartMs ?? d.FireMs)];
            }
            else
            {
                _shown = [.. e.Reminders.Concat(_shown.Where(s => !e.Reminders.Any(r => string.Equals(r.Key, s.Key, StringComparison.Ordinal)))).OrderBy(d => d.StartMs ?? d.FireMs)];
            }
        }

        Post(() => Notify(e.Missed ? nameof(Missed) : nameof(Shown)));
    }

    private void Remove(string key, bool shown, bool missed)
    {
        lock (_lock)
        {
            if (shown)
            {
                _shown = [.. _shown.Where(d => !string.Equals(d.Key, key, StringComparison.Ordinal))];
            }

            if (missed)
            {
                _missed = [.. _missed.Where(d => !string.Equals(d.Key, key, StringComparison.Ordinal))];
            }
        }

        Notify(shown ? nameof(Shown) : nameof(Missed));
        if (shown && missed)
        {
            Notify(nameof(Missed));
        }
    }

    // the timeZone setting, else the machine's zone
    private TimeZoneInfo Zone() => (_prefs is null ? null : CalendarSettings.Read(_prefs.Current).Zone()) ?? _time.LocalTimeZone;

    private void Notify(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));

    private void Post(Action action)
    {
        if (_dispatcher is null || _dispatcher.HasThreadAccess)
        {
            action();
        }
        else
        {
            _dispatcher.Post(action);
        }
    }
}
