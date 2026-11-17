// Mirrors: Rust main.rs:486 PUSH_TYPES (Email, Mailbox, Thread, EmailDelivery, EmailSubmission), :499 start_push (re-open with backoff
// and jitter; everything is synced once each time the stream opens, since changes made while it was down are never pushed), jmap.rs:1108
// watch (closeafter=no, ping=30, 75 s silence = dead), :1614 changed_types; RFC 8620 §7.3 (EventSource), §7.1 (StateChange); Bulwark
// lib/mail-push-listener.ts, lib/plugin-hook-catalog.ts:158 onPushConnectionChange. Owner jmap.

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

    private readonly PushBackoff _backoff;
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
                await PushOnceAsync(ct).ConfigureAwait(false);
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
            _pending.UnionWith(types);
            if (_pending.Count > 0)
            {
                _timer.Change(Coalesce, Timeout.InfiniteTimeSpan);
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
        catch (OperationCanceledException)
        {
            // a sync cut short by sign-out
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

    private async Task PushOnceAsync(CancellationToken ct)
    {
        var open = await _client.OpenEventSourceAsync(PushTypes, PingSeconds, ct).ConfigureAwait(false);
        if (!open.IsOk)
        {
            _client.Trace.Event("PUSH", "failed");
            return;
        }

        await ConnectedAsync(true, ct).ConfigureAwait(false);

        // changes made while nobody listened are never pushed: catch up on every open (Rust main.rs:513)
        Schedule(PushTypes);
        _client.Trace.Event("PUSH", "open");
        var account = _client.Session.MailAccountId;
        try
        {
            await foreach (var ev in EventSourceReader.ReadAsync(open.Value!, _time, EventSourceReader.Deadline, ct).ConfigureAwait(false))
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
        if (_hooks.HasHandlers(HookNames.OnPushConnectionChange))
        {
            await _hooks.NotifyAsync(HookNames.OnPushConnectionChange, new PushConnectionPayload(connected), ct).ConfigureAwait(false);
        }
    }
}
