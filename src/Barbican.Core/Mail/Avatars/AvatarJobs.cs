// Mirrors: Rust avatars.rs:147-172 (the worker pool and its WORK queue with a busy count) and T-023 idle_worker_is_counted_out_under_the_queue_lock;
// D-234 note: the C# queue is a Channel reader, the test asserts the same meaning (busy is zero after the queue drains). Stub from stage F of M2; owner reader.

using Barbican.Core.Common;

// CA1822: stub members become instance members with state when the owner implements them.
#pragma warning disable CA1822

namespace Barbican.Core.Mail.Avatars;

/// <summary>The avatar worker's job queue with a count of busy workers.</summary>
public sealed class AvatarJobs
{
    /// <summary>How many workers are currently counted as busy.</summary>
    public int Busy => throw new PortPendingException("avatars.rs:147 WORK busy count");

    /// <summary>Empties the queue and sets the busy count (the Rust test sets 1 under the lock).</summary>
    internal void Reset(int busy)
    {
        throw new PortPendingException("avatars.rs:147 WORK reset");
    }

    /// <summary>The next job, or null when the queue is empty, in which case the calling worker is counted out (busy decreases).</summary>
    internal string? NextJob()
    {
        throw new PortPendingException("avatars.rs:147 next_job");
    }
}
