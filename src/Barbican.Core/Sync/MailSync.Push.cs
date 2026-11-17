// Mirrors: Rust main.rs:486 PUSH_TYPES (Email, Mailbox, Thread, EmailDelivery, EmailSubmission), :499 start_push (re-open with backoff
// and jitter; everything is synced once each time the stream opens, since changes made while it was down are never pushed), jmap.rs:1108
// watch (closeafter=no, ping=30, 75 s silence = dead), :1614 changed_types; RFC 8620 §7.3 (EventSource), §7.1 (StateChange); Bulwark
// lib/mail-push-listener.ts, lib/plugin-hook-catalog.ts:158 onPushConnectionChange. Owner jmap.

using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Push;
using Barbican.Core.Mail.Model;
using Barbican.Core.Plugins.Hooks;

namespace Barbican.Core.Sync;

public sealed partial class MailSync
{
    /// <summary>The data types followed over push.</summary>
    public static readonly IReadOnlyList<string> PushTypes = ["Email", "Mailbox", "Thread", "EmailDelivery", "EmailSubmission"];

    /// <summary>The keep-alive interval asked of the server (Stalwart's minimum).</summary>
    public const int PingSeconds = 30;

    /// <summary>The longest a burst of pushes may postpone the sync: a steady stream must not hold it back for ever (Fable F-15).</summary>
    public static readonly TimeSpan MaxCoalesce = TimeSpan.FromSeconds(3);

    /// <summary>How often the lists are synced when the server offers no push (Fable F-15).</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);

    private readonly PushBackoff _backoff;
    private long _firstPending;
    private bool _authReported;
    private readonly ITimer _timer;
    private readonly HashSet<string> _pending = new(StringComparer.Ordinal);
    private readonly List<Task> _inflight = [];
    private CancellationToken _runToken;

    /// <inheritdoc/>
    public async Task RunAsync(CancellationToken ct)
    {
        lock (_lock)
        {
            _runToken = ct;
        }

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var started = _time.GetTimestamp();
                if (!await PushOnceAsync(ct).ConfigureAwait(false))
                {
                    // the server has no event source: poll instead of never updating
                    await PollAsync(ct).ConfigureAwait(false);
                    return;
                }

                var wait = _backoff.Next(_time.GetElapsedTime(started));
                await Task.Delay(wait, _time, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // signed out or closed
        }
        finally
        {
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            await DrainAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Queues a sync of <paramref name="types"/>; pushes within <see cref="Coalesce"/> of each other share one sync.</summary>
    public void Schedule(IEnumerable<string> types)
    {
        ArgumentNullException.ThrowIfNull(types);
        lock (_lock)
        {
            var now = _time.GetTimestamp();
            if (_pending.Count == 0)
            {
                _firstPending = now;
            }

            _pending.UnionWith(types);
            if (_pending.Count > 0)
            {
                var wait = MaxCoalesce - _time.GetElapsedTime(_firstPending, now);
                _timer.Change(wait < Coalesce ? (wait < TimeSpan.Zero ? TimeSpan.Zero : wait) : Coalesce, Timeout.InfiniteTimeSpan);
            }
        }
    }

    /// <summary>Waits for the syncs already started.</summary>
    public async Task DrainAsync()
    {
        Task[] running;
        lock (_lock)
        {
            running = [.. _inflight];
        }

        try
        {
            await Task.WhenAll(running).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // a sync's own failure is reported through onSyncStateChange; draining must not rethrow it out of RunAsync (Fable F-15)
        catch (Exception)
#pragma warning restore CA1031
        {
            // a sync cut short by sign-out, or one that failed and said so
        }
    }

    private void OnCoalesced()
    {
        HashSet<string> types;
        CancellationToken ct;
        lock (_lock)
        {
            types = [.. _pending];
            _pending.Clear();
            ct = _runToken;
            _inflight.RemoveAll(t => t.IsCompleted);
        }

        if (types.Count == 0 || ct.IsCancellationRequested)
        {
            return;
        }

        var task = SyncNowAsync(types, ct);
        lock (_lock)
        {
            _inflight.Add(task);
        }
    }

    /// <summary>Syncs on a timer for a server without push.</summary>
    private async Task PollAsync(CancellationToken ct)
    {
        _client.Trace.Event("PUSH", "polling");
        while (!ct.IsCancellationRequested)
        {
            Schedule(PushTypes);
            await Task.Delay(PollInterval, _time, ct).ConfigureAwait(false);
        }
    }

    /// <summary>One stream, from open to end; false when the server offers no event source at all.</summary>
    private async Task<bool> PushOnceAsync(CancellationToken ct)
    {
        var open = await _client.OpenEventSourceAsync(PushTypes, PingSeconds, ct).ConfigureAwait(false);
        if (!open.IsOk)
        {
            _client.Trace.Event("PUSH", "failed");
            switch (open.Error)
            {
                case AuthError auth when !_authReported:
                    // retried with backoff as before, but the user hears of it once (Fable F-15)
                    _authReported = true;
                    Emit(new SyncProblemEvent(auth.Message, auth.NeedsSignIn));
                    break;
                case TransportError { Retryable: false }:
                    return false;
                default:
                    break;
            }

            return true;
        }

        _authReported = false;
        // the reader disposes the stream when it ends; this also covers a hook that throws before reading starts
        var stream = open.Value!;
        await using var owned = stream.ConfigureAwait(false);
        await ConnectedAsync(true, ct).ConfigureAwait(false);

        // changes made while nobody listened are never pushed: catch up on every open (Rust main.rs:513)
        Schedule(PushTypes);
        _client.Trace.Event("PUSH", "open");
        var account = _client.Session.MailAccountId;
        try
        {
            await foreach (var ev in EventSourceReader.ReadAsync(stream, _time, EventSourceReader.Deadline, ct).ConfigureAwait(false))
            {
                OnPush(ev, account);
            }

            _client.Trace.Event("PUSH", "closed");
        }
        catch (TimeoutException)
        {
            _client.Trace.Event("PUSH", "silent");
        }
        catch (Exception e) when (e is IOException or HttpRequestException)
        {
            _client.Trace.Event("PUSH", "dropped");
        }
        finally
        {
            await ConnectedAsync(false, CancellationToken.None).ConfigureAwait(false);
        }

        return true;
    }

    private void OnPush(PushEvent ev, string account)
    {
        switch (ev)
        {
            case PushState s:
                var types = PushChanges.ChangedTypes(s.Change, account).Keys.Order(StringComparer.Ordinal).ToList();
                _client.Trace.Event("PUSH", "state " + string.Join(",", types));
                Schedule(types);
                break;
            case PushPing:
                _client.Trace.Event("PUSH", "ping");
                break;
            default:
                break;
        }
    }

    private async Task ConnectedAsync(bool connected, CancellationToken ct)
    {
        Emit(new PushStateEvent(connected));
        await NotifyAsync(HookNames.OnPushConnectionChange, new PushConnectionPayload(connected), ct).ConfigureAwait(false);
    }
}
