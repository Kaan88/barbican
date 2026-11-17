// Mirrors: Rust jmap.rs:246 (429/503 on a read-only batch: wait Retry-After seconds, default 2, at most 10, then retry once);
// RFC 9110 §10.2.3 (Retry-After is delay-seconds or an HTTP-date); RFC 8620 §3.6.1 (serverUnavailable / 429: retry later). Owner jmap.

using System.Net;
using System.Net.Http.Headers;

namespace Barbican.Core.Jmap.Transport;

/// <summary>When a read-only batch may be sent again.</summary>
public static class RetryAfter
{
    private static readonly TimeSpan Default = TimeSpan.FromSeconds(2);

    /// <summary>True for the statuses that ask for a later retry (429, 503).</summary>
    public static bool IsRetryStatus(HttpStatusCode status) => status is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable;

    /// <summary>The wait the server asked for, defaulting to 2 s and capped at <paramref name="max"/>.</summary>
    public static TimeSpan Delay(RetryConditionHeaderValue? header, DateTimeOffset now, TimeSpan max)
    {
        var wait = header switch
        {
            { Delta: { } d } => d,
            { Date: { } at } => at - now,
            _ => Default,
        };
        if (wait < TimeSpan.Zero)
        {
            wait = TimeSpan.Zero;
        }

        return wait > max ? max : wait;
    }
}
