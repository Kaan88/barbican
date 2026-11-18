// Mirrors: Rust cal_extra.rs:840 sub_refresh (the manual refresh) and Bulwark lib/calendar-subscription-sync.ts (each subscription is refreshed when its interval has passed since the last run); D-M4-I01.
// Nothing ran the stored interval before: this service wakes on TimeProvider (the same pattern as ReminderScheduler), and refreshes every subscription of every signed-in login whose interval has elapsed.

using Barbican.Core.Accounts;

namespace Barbican.Core.Calendar.Feeds;

/// <summary>Refreshes webcal subscriptions on their own interval.</summary>
public sealed class CalendarSubscriptionRefresher(IAccountManager accounts, ICalendarSubscriptions subscriptions, TimeProvider time) : IDisposable
{
    /// <summary>How often the due subscriptions are looked for (the intervals themselves are 15 minutes and up).</summary>
    public static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(1);

    private readonly Lock _lock = new();
    private readonly Dictionary<string, DateTimeOffset> _last = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ITimer? _timer;
    private CancellationTokenSource _stop = new();

    /// <summary>Starts the timer; idempotent. The first run waits for the first tick, so start-up does not fetch feeds.</summary>
    public void Start()
    {
        lock (_lock)
        {
            if (_timer is not null)
            {
                return;
            }

            if (_stop.IsCancellationRequested)
            {
                _stop.Dispose();
                _stop = new CancellationTokenSource();
            }

            var token = _stop.Token;
            _timer = time.CreateTimer(_ => _ = Task.Run(() => TickAsync(token), token), null, CheckEvery, CheckEvery);
        }
    }

    /// <summary>Stops the timer.</summary>
    public void Shutdown()
    {
        lock (_lock)
        {
            _timer?.Dispose();
            _timer = null;
        }

        _stop.Cancel();
    }

    /// <inheritdoc/>
    public void Dispose() => Shutdown();

    private async Task TickAsync(CancellationToken ct)
    {
        try
        {
            _ = await RefreshDueAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // shut down
        }
    }

    /// <summary>Refreshes what is due now (what the timer runs); returns how many subscriptions were refreshed. Overlapping runs are skipped.</summary>
    public async Task<int> RefreshDueAsync(CancellationToken ct)
    {
        if (!await _gate.WaitAsync(0, ct).ConfigureAwait(false))
        {
            return 0;
        }

        try
        {
            var done = 0;
            foreach (var account in accounts.Accounts)
            {
                var list = await subscriptions.ListAsync(account.Key, ct).ConfigureAwait(false);
                foreach (var sub in list)
                {
                    var key = account.Key + "\n" + sub.CalendarId;
                    var now = time.GetUtcNow();
                    DateTimeOffset last;
                    lock (_lock)
                    {
                        if (!_last.TryGetValue(key, out last))
                        {
                            // never refreshed in this run: the interval counts from now, so a start-up never fetches every feed at once
                            _last[key] = now;
                            continue;
                        }
                    }

                    if (now - last < TimeSpan.FromMinutes(Math.Max(1, sub.IntervalMinutes)))
                    {
                        continue;
                    }

                    lock (_lock)
                    {
                        _last[key] = now; // a failed refresh waits for the next interval too
                    }

                    _ = await subscriptions.RefreshAsync(account.Key, sub.CalendarId, ct).ConfigureAwait(false);
                    done++;
                }
            }

            return done;
        }
        finally
        {
            _ = _gate.Release();
        }
    }
}
