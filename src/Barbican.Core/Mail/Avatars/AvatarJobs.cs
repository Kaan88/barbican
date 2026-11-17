// Mirrors: Rust avatars.rs:56 WORK (the fetch queue and the number of running workers under one lock), :147 enqueue, :166 next_job, and T-023
// idle_worker_is_counted_out_under_the_queue_lock: a worker that finds the queue empty is counted out under the same lock, so a key pushed in the
// same moment always finds a worker (either the one still counted or a new one). D-234 note: kept as a lock and a queue (a Channel would need its
// own busy count with the same race). Owner reader.

namespace Barbican.Core.Mail.Avatars;

/// <summary>The avatar worker's job queue with a count of busy workers.</summary>
public sealed class AvatarJobs(int maxWorkers = 4)
{
    private readonly Lock _lock = new();
    private readonly Queue<string> _queue = new();
    private int _busy;

    /// <summary>How many workers are currently counted as busy.</summary>
    public int Busy
    {
        get
        {
            lock (_lock)
            {
                return _busy;
            }
        }
    }

    /// <summary>How many jobs wait.</summary>
    public int Waiting
    {
        get
        {
            lock (_lock)
            {
                return _queue.Count;
            }
        }
    }

    /// <summary>Queues a job; true when the caller must start one more worker (it is counted busy already).</summary>
    public bool Enqueue(string job)
    {
        ArgumentNullException.ThrowIfNull(job);
        lock (_lock)
        {
            _queue.Enqueue(job);
            if (_busy >= maxWorkers)
            {
                return false;
            }

            _busy++;
            return true;
        }
    }

    /// <summary>Drops every waiting job (a refresh); running workers finish their current one.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _queue.Clear();
        }
    }

    /// <summary>The next job, or null when the queue is empty, in which case the calling worker is counted out (busy decreases).</summary>
    public string? NextJob()
    {
        lock (_lock)
        {
            if (_queue.TryDequeue(out var job))
            {
                return job;
            }

            _busy = Math.Max(0, _busy - 1);
            return null;
        }
    }

    /// <summary>Empties the queue and sets the busy count (the Rust test sets 1 under the lock).</summary>
    internal void Reset(int busy)
    {
        lock (_lock)
        {
            _queue.Clear();
            _busy = busy;
        }
    }
}
