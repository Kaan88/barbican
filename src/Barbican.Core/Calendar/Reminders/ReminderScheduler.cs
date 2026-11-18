// Mirrors: Rust reminders.rs:351 start, :366 stop, :373 fetch, :412 check, :499 act, :523 missed_done; Bulwark hooks/use-calendar-alerts.ts (poll, snooze, acknowledge, stale threshold), spec B-CAL-028..035; hooks onBeforeCalendarAlert,
// onCalendarAlert, onCalendarAlertAcknowledge (lib/plugin-hook-catalog.ts:185-187). Every signed-in login is watched, not only the active one (Rust watched the active client only: a second login's reminders never fired). Owner cal-data.

using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Storage;
using Barbican.Core.Sync;

namespace Barbican.Core.Calendar.Reminders;

/// <summary>The default <see cref="IReminderScheduler"/>.</summary>
public sealed partial class ReminderScheduler : IReminderScheduler, IDisposable
{
    /// <summary>How often what is due is checked (Rust woke at the next reminder, at most a minute away; the plan fixes 15 s).</summary>
    public static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(15);

    /// <summary>How often the events are read again (Rust REFETCH_SECS).</summary>
    public static readonly TimeSpan RefetchEvery = TimeSpan.FromSeconds(300);

    private readonly IAccountManager _accounts;
    private readonly IReminderStateStore _state;
    private readonly IHookBus _hooks;
    private readonly TimeProvider _time;
    private readonly IPrefsStore _prefs;
    private readonly ICalendarStore? _store;
    private readonly ICalendarPush? _push;
    private readonly Lock _lock = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, Run> _runs = new(StringComparer.Ordinal);
    private ITimer? _timer;
    private Task _inflight = Task.CompletedTask;
    private CancellationTokenSource _stop = new();

    public ReminderScheduler(IAccountManager accounts, IReminderStateStore state, IHookBus hooks, TimeProvider time, IPrefsStore prefs, ICalendarStore? store = null, ICalendarPush? push = null)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(hooks);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(prefs);
        _accounts = accounts;
        _state = state;
        _hooks = hooks;
        _time = time;
        _prefs = prefs;
        _store = store;
        _push = push;
    }

    /// <inheritdoc/>
    public event EventHandler<ReminderEventArgs>? Due;

    /// <inheritdoc/>
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

            _timer = _time.CreateTimer(_ => Kick(), null, CheckEvery, CheckEvery);
            if (_store is not null)
            {
                _store.Changed += OnStoreChanged;
            }

            if (_push is not null)
            {
                _push.Changed += OnPush;
            }
        }

        Kick();
    }

    /// <inheritdoc/>
    public void Shutdown()
    {
        lock (_lock)
        {
            _timer?.Dispose();
            _timer = null;
            if (_store is not null)
            {
                _store.Changed -= OnStoreChanged;
            }

            if (_push is not null)
            {
                _push.Changed -= OnPush;
            }

            _runs.Clear();
        }

        _stop.Cancel();
    }

    /// <inheritdoc/>
    public void Dispose() => Shutdown();

    /// <summary>Waits for the checks that were started and are running (tests, shutdown).</summary>
    public async Task DrainAsync()
    {
        Task running;
        lock (_lock)
        {
            running = _inflight;
        }

#pragma warning disable VSTHRD003 // the check this scheduler started itself (Kick)
        await running.ConfigureAwait(false);
#pragma warning restore VSTHRD003
        await _gate.WaitAsync().ConfigureAwait(false);
        _ = _gate.Release();
    }

    /// <summary>One check of every login now (what the timer runs); overlapping checks are skipped.</summary>
    public async Task CheckNowAsync(CancellationToken ct)
    {
        if (!await _gate.WaitAsync(0, ct).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            await CheckAllAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    // a timer tick or a start: the check runs on the pool and is remembered so a test or the shutdown can wait for it
    private void Kick()
    {
        var token = _stop.Token;
        var task = Task.Run(async () =>
        {
            try
            {
                await CheckNowAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // shut down
            }
        });
        lock (_lock)
        {
            _inflight = task;
        }
    }

    private void OnStoreChanged(object? sender, CalendarChangedEventArgs e) => MarkDirty(e.AccountKey);

    private void OnPush(object? sender, CalendarPushEventArgs e)
    {
        if (_accounts.Accounts.Select(a => a.Key).FirstOrDefault(k => ReferenceEquals(_accounts.SessionFor(k)?.Client, e.Client)) is { } key)
        {
            MarkDirty(key);
        }
    }

    // a calendar change (push, a save): the events are read again on the next check
    private void MarkDirty(string key)
    {
        lock (_lock)
        {
            if (_runs.TryGetValue(key, out var run))
            {
                run.Dirty = true;
            }
        }
    }

    private long NowMs() => _time.GetUtcNow().ToUnixTimeMilliseconds();

    /// <summary>What one login's reminders hold between checks.</summary>
    private sealed class Run(string owner)
    {
        public string Owner { get; } = owner;

        public IReadOnlyList<JsonNode> Events { get; set; } = [];

        public IReadOnlyList<JsonNode> Calendars { get; set; } = [];

        public long? FetchedMs { get; set; }

        public bool Dirty { get; set; }

        public bool CheckedMissed { get; set; }

        /// <summary>Reminders on screen (shown, not dismissed): key to the reminder.</summary>
        public Dictionary<string, DueReminder> Shown { get; } = new(StringComparer.Ordinal);

        /// <summary>Snoozed: key to the time (UTC ms) it is shown again.</summary>
        public Dictionary<string, long> Snoozed { get; } = new(StringComparer.Ordinal);

        public List<DueReminder> MissedList { get; } = [];
    }
}
