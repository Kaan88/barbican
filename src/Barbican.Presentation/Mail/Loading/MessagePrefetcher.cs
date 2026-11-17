// Mirrors: Rust msgload.rs prefetch :320, prefetch_top :351, fetch_ahead :357, PREFETCHING :30 (tag rust-final): messages already cached or on
// their way are skipped; the jobs run one after another off the UI thread. Additions (M1-plan §6.5): a batch is cancelled when the direction
// of travel turns, and an open waits for the prefetch of its own message instead of asking twice (Rust polled PREFETCHING every 8 ms).
// Bulwark lib/email-prefetch.ts. Owner body.

using System.Collections.Concurrent;

namespace Barbican.Presentation.Mail.Loading;

/// <summary>Warms the body cache around the open message, in the direction of travel.</summary>
public sealed class MessagePrefetcher(MessageFetcher fetcher) : IDisposable
{
    private readonly ConcurrentDictionary<string, Task> _inFlight = new(StringComparer.Ordinal);
    private TravelState _travel = TravelState.Start;
    private CancellationTokenSource? _batch;
    private bool? _batchBack;

    /// <summary>The travel state after the last prefetch (tests).</summary>
    public TravelState Travel => _travel;

    /// <summary>Messages queued or loading right now.</summary>
    public IReadOnlyCollection<string> InFlight => [.. _inFlight.Keys];

    /// <summary>The prefetch of a message, when one is on its way.</summary>
    public bool TryGetPending(string emailId, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Task? pending) => _inFlight.TryGetValue(emailId, out pending);

    /// <summary>Prefetches around <paramref name="emailId"/>: its list neighbours in the direction of travel and its conversation neighbours.</summary>
    public IReadOnlyList<string> Around(string emailId, IReadOnlyList<MessageRowViewModel> rows, IReadOnlyList<ConversationItem> strip, PrefetchContext context)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(strip);
        ArgumentNullException.ThrowIfNull(context);
        var ids = new List<string>();
        var at = -1;
        for (var i = 0; i < rows.Count; i++)
        {
            if (string.Equals(rows[i].Id, emailId, StringComparison.Ordinal))
            {
                at = i;
                break;
            }
        }

        if (at >= 0)
        {
            var (indices, travel) = PrefetchPlan.Around(at, rows.Count, _travel);
            _travel = travel;
            ids.AddRange(indices.Select(j => rows[j].Id));
        }

        ids.AddRange(PrefetchPlan.ConversationNeighbours(strip));
        ids.RemoveAll(x => string.Equals(x, emailId, StringComparison.Ordinal));
        return Run(ids, context, at >= 0 ? _travel.Back : null);
    }

    /// <summary>A list was loaded: warm its first messages, so opening the top one needs no round trip either.</summary>
    public IReadOnlyList<string> Top(IReadOnlyList<MessageRowViewModel> rows, PrefetchContext context)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return Run([.. rows.Take(PrefetchPlan.Top).Select(r => r.Id)], context, null);
    }

    /// <summary>Stops every queued prefetch (account change, close).</summary>
    public void CancelAll()
    {
        // cancelled, not disposed: running jobs still hold its token
        _batch?.Cancel();
        _batch = null;
        _batchBack = null;
        _travel = TravelState.Start;
    }

    public void Dispose() => CancelAll();

    private List<string> Run(List<string> ids, PrefetchContext context, bool? back)
    {
        // the user turned round: what was queued for the other way is not needed any more
        if (back is { } b && _batchBack is { } was && b != was)
        {
            _batch?.Cancel();
            _batch = null;
        }

        if (back is not null)
        {
            _batchBack = back;
        }

        var jobs = ids.Distinct(StringComparer.Ordinal)
            .Where(id => !fetcher.Contains(context.AccountKey, id) && !_inFlight.ContainsKey(id))
            .ToList();
        if (jobs.Count == 0)
        {
            return jobs;
        }

        _batch ??= new CancellationTokenSource();
        var ct = _batch.Token;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var previous = Task.CompletedTask;
        foreach (var id in jobs)
        {
            previous = EnqueueAsync(id, previous, gate.Task, context, ct);
        }

        gate.SetResult();
        return jobs;
    }

    // One prefetch, after the one before it (whatever became of that), once the batch is registered.
    private Task EnqueueAsync(string id, Task after, Task gate, PrefetchContext context, CancellationToken ct)
    {
#pragma warning disable VSTHRD003 // the gate and the job before are this batch's own tasks; the jobs run in order on the thread pool
        var job = Task.Run(
            async () =>
            {
                await gate.ConfigureAwait(false);
                try
                {
                    await after.ConfigureAwait(false);
                }
#pragma warning disable CA1031 // the job before failing or being cancelled does not stop this one
                catch (Exception)
#pragma warning restore CA1031
                {
                }

                try
                {
                    ct.ThrowIfCancellationRequested();
                    _ = await fetcher.FetchAsync(context.AccountKey, context.Mail, id, context.WantThread, prefetch: true, ct).ConfigureAwait(false);
                }
                finally
                {
                    _inFlight.TryRemove(id, out _);
                }
            },
            CancellationToken.None);
#pragma warning restore VSTHRD003
        _inFlight[id] = job;
        return job;
    }
}
