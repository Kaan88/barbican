// Mirrors: RFC 8620 7.3 (EventSource, StateChange), PORT-PLAN section 3.4 (push type FileNode), Rust main.rs:499 start_push (reconnect with backoff; PushBackoff), Bulwark lib/mail-push-listener.ts; Stalwart crates/jmap/src/api/event_source.rs.
// The Files store follows FileNode on a stream of its own (the mail engine of M1 is not touched: its push type list stays mail and calendars). G-044 (files live). Owner files-data.

using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Push;
using Barbican.Core.Sync;

namespace Barbican.Core.Files;

public sealed partial class FileStore
{
    /// <summary>The wait that gathers a burst of pushes into one /changes (as the mail engine).</summary>
    public static readonly TimeSpan Coalesce = TimeSpan.FromMilliseconds(700);

    private static readonly string[] PushTypes = ["FileNode"];

    // a login with files follows their state string while the store holds its nodes (the stream is opened once per login)
    private void EnsureWatching(string accountKey, IJmapClient client)
    {
        if (!_watchPush || string.IsNullOrEmpty(client.Session.EventSourceUrl))
        {
            return;
        }

        CancellationTokenSource cts;
        lock (_lock)
        {
            if (_watchers.ContainsKey(accountKey) || _stop.IsCancellationRequested)
            {
                return;
            }

            cts = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            _watchers[accountKey] = cts;
        }

        _ = Task.Run(() => WatchAsync(accountKey, client, cts.Token), CancellationToken.None);
    }

#pragma warning disable CA1031 // a push stream that fails is re-opened with backoff; nothing here may end the app
    private async Task WatchAsync(string accountKey, IJmapClient client, CancellationToken ct)
    {
        var backoff = PushBackoff.WithRandomJitter();
        var accounts = FileAccounts.All(client.Session);
        while (!ct.IsCancellationRequested)
        {
            var started = _time.GetTimestamp();
            try
            {
                var open = await client.OpenEventSourceAsync(PushTypes, MailSync.PingSeconds, ct).ConfigureAwait(false);
                if (open.IsOk)
                {
                    // a change made while the stream was down is caught up with /changes
                    _ = await RefreshAsync(accountKey, ct).ConfigureAwait(false);
                    await foreach (var ev in EventSourceReader.ReadAsync(open.Value!, _time, EventSourceReader.Deadline, ct).ConfigureAwait(false))
                    {
                        if (ev is PushState s && accounts.Any(a => PushChanges.ChangedTypes(s.Change, a).ContainsKey("FileNode")))
                        {
                            await Task.Delay(Coalesce, _time, ct).ConfigureAwait(false);
                            _ = await RefreshAsync(accountKey, ct).ConfigureAwait(false);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e) when (e is TimeoutException or IOException or HttpRequestException or InvalidOperationException)
            {
                // the stream died: wait and open it again
            }

            try
            {
                await Task.Delay(backoff.Next(_time.GetElapsedTime(started)), _time, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
#pragma warning restore CA1031
}
