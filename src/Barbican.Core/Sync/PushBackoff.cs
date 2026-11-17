// Mirrors: Rust main.rs:499 start_push (re-open after `backoff*1000 + jitter % (backoff*500)` ms, backoff 2 s doubling to 300 s, back to
// 2 s after a stream that stayed up more than 120 s); Bulwark lib/mail-push-listener.ts (reconnect with backoff). Owner jmap.

namespace Barbican.Core.Sync;

/// <summary>The reconnect schedule of the push stream.</summary>
public sealed class PushBackoff(Func<int, int> jitter)
{
    /// <summary>The first wait, in seconds.</summary>
    public const int First = 2;

    /// <summary>The longest wait, in seconds.</summary>
    public const int Max = 300;

    /// <summary>A stream that stayed up this long resets the backoff.</summary>
    public static readonly TimeSpan Healthy = TimeSpan.FromSeconds(120);

    /// <summary>The current backoff, in seconds.</summary>
    public int Seconds { get; private set; } = First;

    /// <summary>A schedule with random jitter.</summary>
    public static PushBackoff WithRandomJitter() => new(n => n <= 0 ? 0 : Random.Shared.Next(n));

    /// <summary>
    /// The wait before the next open, after a stream that stayed up for <paramref name="upFor"/>; the backoff then doubles (to at most
    /// <see cref="Max"/>). The wait lies in [backoff, 1.5 × backoff).
    /// </summary>
    public TimeSpan Next(TimeSpan upFor)
    {
        if (upFor > Healthy)
        {
            Seconds = First;
        }

        var spread = Seconds * 500;
        var wait = TimeSpan.FromMilliseconds((Seconds * 1000L) + Math.Clamp(jitter(spread), 0, spread - 1));
        Seconds = Math.Min(Seconds * 2, Max);
        return wait;
    }
}
