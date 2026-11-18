// Mirrors: Rust reminders.rs:412 check (show what is due, list what was missed once per run, snoozed ones come back), :499 act (dismiss, snooze), :523 missed_done; Bulwark hooks/use-calendar-alerts.ts. Owner cal-data.

using Barbican.Core.Jmap;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;

namespace Barbican.Core.Calendar.Reminders;

public sealed partial class ReminderScheduler
{
    /// <inheritdoc/>
    public async Task DismissAsync(IReadOnlyList<DueReminder> reminders, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reminders);
        var byOwner = new Dictionary<string, List<DueReminder>>(StringComparer.Ordinal);
        lock (_lock)
        {
            foreach (var d in reminders)
            {
                foreach (var run in _runs.Values.Where(r => r.Shown.ContainsKey(d.Key) || r.MissedList.Exists(m => string.Equals(m.Key, d.Key, StringComparison.Ordinal))))
                {
                    _ = run.Shown.Remove(d.Key);
                    _ = run.Snoozed.Remove(d.Key);
                    _ = run.MissedList.RemoveAll(m => string.Equals(m.Key, d.Key, StringComparison.Ordinal));
                    if (!byOwner.TryGetValue(run.Owner, out var list))
                    {
                        byOwner[run.Owner] = list = [];
                    }

                    list.Add(d);
                }
            }
        }

        var now = NowMs();
        foreach (var (owner, list) in byOwner)
        {
            await _state.AcknowledgeAsync(owner, [.. list.Select(d => (d.Event, d.FireMs, d.Alert))], now, ct).ConfigureAwait(false);
            foreach (var d in list)
            {
                _hooks.Fire(HookNames.OnCalendarAlertAcknowledge, () => new CalendarAlertAcknowledgePayload(d.Alert, d.Task ? null : CalendarIds.BaseId(d.Event), d.Task ? d.Event : null));
            }
        }
    }

    /// <inheritdoc/>
    public void Snooze(IReadOnlyList<DueReminder> reminders, int minutes)
    {
        ArgumentNullException.ThrowIfNull(reminders);
        var at = NowMs() + (minutes * 60_000L);
        lock (_lock)
        {
            foreach (var d in reminders)
            {
                foreach (var run in _runs.Values.Where(r => r.Shown.ContainsKey(d.Key)))
                {
                    run.Snoozed[d.Key] = at;
                }
            }
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<DueReminder>> MissedAsync(CancellationToken ct)
    {
        // the first check of every login decides what was missed: wait for it
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!AllChecked())
            {
                await CheckAllAsync(ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _ = _gate.Release();
        }

        lock (_lock)
        {
            return [.. _runs.Values.SelectMany(r => r.MissedList)];
        }
    }

    private bool AllChecked()
    {
        lock (_lock)
        {
            return _runs.Count > 0 && _runs.Values.All(r => r.CheckedMissed);
        }
    }

    private List<Run> SyncRuns()
    {
        var keys = _accounts.Accounts.Select(a => a.Key).Where(k => _accounts.SessionFor(k)?.Client.Session.Has(JmapCapabilities.Calendars) == true).ToList();
        lock (_lock)
        {
            foreach (var gone in _runs.Keys.Where(k => !keys.Contains(k, StringComparer.Ordinal)).ToList())
            {
                _ = _runs.Remove(gone);
            }

            foreach (var k in keys.Where(k => !_runs.ContainsKey(k)))
            {
                _runs[k] = new Run(k);
            }

            return [.. _runs.Values];
        }
    }

#pragma warning disable CA1031 // one login's failure (a server that is down, a hook that throws) must not stop the reminders of the others
    private async Task CheckAllAsync(CancellationToken ct)
    {
        foreach (var run in SyncRuns())
        {
            try
            {
                await CheckAsync(run, ct).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // checked again in 15 seconds
            }
        }
    }
#pragma warning restore CA1031

    /// <summary>Show what is due now, list what was missed (once per run), bring snoozed ones back (Rust check).</summary>
    private async Task CheckAsync(Run run, CancellationToken ct)
    {
        var now = NowMs();
        var settings = CalendarSettings.Read(_prefs.Current);
        var sound = settings.NotificationSound && !_prefs.Current.Bool("doNotDisturb", false);
        if (run.FetchedMs is null || run.Dirty || now - run.FetchedMs >= (long)RefetchEvery.TotalMilliseconds)
        {
            _ = await FetchAsync(run, now, ct).ConfigureAwait(false);
        }

        if (run.FetchedMs is null)
        {
            return;
        }

        var last = await _state.LastCheckAsync(run.Owner, ct).ConfigureAwait(false);
        if (settings.NotificationsEnabled)
        {
            var acked = await _state.AcknowledgedAsync(run.Owner, ct).ConfigureAwait(false);
            var first = !run.CheckedMissed;
            var from = first ? (last is { } l ? Math.Max(l, now - ReminderRules.MissedWindowMs) : now - ReminderRules.StaleMs) : now - ReminderRules.StaleMs;
            var due = ReminderRules.DueBetween(run.Events, run.Calendars, Math.Min(from, now - ReminderRules.StaleMs), now + 1, settings.TasksEnabled, settings.Zone());
            if (first)
            {
                await ListMissedAsync(run, due, acked, last, now, sound, ct).ConfigureAwait(false);
            }

            await ShowAsync(run, due, acked, now, sound, ct).ConfigureAwait(false);
        }

        run.CheckedMissed = true;
        await _state.SetLastCheckAsync(run.Owner, now, ct).ConfigureAwait(false);
    }

    // missed while closed: listed once, then dealt with
    private async Task ListMissedAsync(Run run, IReadOnlyList<DueReminder> due, IReadOnlySet<string> acked, long? last, long now, bool sound, CancellationToken ct)
    {
        var missed = ReminderRules.Missed(due, acked, last, now);
        if (missed.Count == 0)
        {
            return;
        }

        await _state.AcknowledgeAsync(run.Owner, [.. missed.Select(d => (d.Event, d.FireMs, d.Alert))], now, ct).ConfigureAwait(false);
        lock (_lock)
        {
            run.MissedList.AddRange(missed);
        }

        Raise(new ReminderEventArgs(run.Owner, missed, true) { Sound = sound });
    }

    private async Task ShowAsync(Run run, IReadOnlyList<DueReminder> due, IReadOnlySet<string> acked, long now, bool sound, CancellationToken ct)
    {
        List<DueReminder> fresh;
        List<DueReminder> back = [];
        lock (_lock)
        {
            fresh = [.. due.Where(d => d.FireMs > now - ReminderRules.StaleMs && d.FireMs <= now && !acked.Contains(d.Key) && !run.Shown.ContainsKey(d.Key))];

            // snoozed ones whose time has come back
            foreach (var key in run.Snoozed.Where(kv => kv.Value <= now).Select(kv => kv.Key).ToList())
            {
                _ = run.Snoozed.Remove(key);
                if (run.Shown.TryGetValue(key, out var d))
                {
                    back.Add(d);
                }
            }
        }

        var show = new List<DueReminder>(back);
        if (fresh.Count > 0)
        {
            await _state.AcknowledgeAsync(run.Owner, [.. fresh.Select(d => (d.Event, d.FireMs, d.Alert))], now, ct).ConfigureAwait(false);
        }

        foreach (var d in fresh)
        {
            // a plugin that takes the reminder over shows it itself: no popup, but the alert still happened
            var ours = await _hooks.AllowAsync(HookNames.OnBeforeCalendarAlert, () => Payload(d), ct).ConfigureAwait(false);
            _hooks.Fire(HookNames.OnCalendarAlert, () => Payload(d));
            if (ours)
            {
                lock (_lock)
                {
                    run.Shown[d.Key] = d;
                }

                show.Add(d);
            }
        }

        if (show.Count > 0)
        {
            Raise(new ReminderEventArgs(run.Owner, show, false) { Sound = sound });
        }
    }

    private static CalendarAlertPayload Payload(DueReminder d) =>
        new(d.Alert, d.Task ? null : CalendarIds.BaseId(d.Event), d.Task ? null : d.Title, d.Task ? d.Event : null, d.Task ? d.Title : null, d.FireMs);

#pragma warning disable CA1031 // a subscriber that throws must not stop the reminders behind it
    private void Raise(ReminderEventArgs e)
    {
        try
        {
            Due?.Invoke(this, e);
        }
        catch (Exception)
        {
            // the popup and the notification service are not the scheduler's concern
        }
    }
#pragma warning restore CA1031
}
