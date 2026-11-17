// Mirrors: Rust jmap.rs:1454 reschedule_fallbacks (and :985 reschedule_submission, which tries them in order). Owner jmap.

namespace Barbican.Core.Jmap.Submission;

/// <summary>Orders the fallbacks of a reschedule.</summary>
public static class ReschedulePlanner
{
    /// <summary>
    /// Recreate at the old time while it is still ahead by more than a second (a time already past would send at once, which nobody
    /// asked for), then restore the draft, so the message never stays in Sent looking sent.
    /// </summary>
    public static IReadOnlyList<RescheduleFallback> Fallbacks(long? oldAtMs, long nowMs)
    {
        var list = new List<RescheduleFallback>(2);
        if (oldAtMs is { } t && t > nowMs + 1000)
        {
            list.Add(new RecreateAt(t));
        }

        list.Add(new RestoreDraft());
        return list;
    }
}
