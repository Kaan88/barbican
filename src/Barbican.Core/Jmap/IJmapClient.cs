// Mirrors: Bulwark lib/jmap/client.ts (connect :2150, request :2384, getBlobDownloadUrl :6876, getEventSourceUrl :7391); Rust
// jmap.rs:7 Client (request :272, download_from :772, account_cap :306, watch :1108); M1-plan §4.1. Owner jmap.

using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Transport;

namespace Barbican.Core.Jmap;

/// <summary>One connection to a JMAP server for one login (session, requests, downloads, the event stream).</summary>
public interface IJmapClient
{
    /// <summary>The live session; refreshed when <c>sessionState</c> changes.</summary>
    JmapSession Session { get; }

    /// <summary>True for the read-only account: anything outside the allow-list returns a ReadOnlyViolation.</summary>
    bool IsReadOnly { get; }

    /// <summary>The method-name trace of this client (off unless <c>BARBICAN_JMAP_TRACE</c> or the options name a file).</summary>
    JmapTrace Trace => JmapTrace.Off;

    /// <summary>Sends one JMAP request (split by <c>maxCallsInRequest</c> unless the calls are linked).</summary>
    Task<Result<JmapResponse>> SendAsync(JmapRequest request, CancellationToken ct);

    /// <summary>Downloads a blob through the session's download URL template.</summary>
    Task<Result<Stream>> DownloadAsync(string accountId, string blobId, string name, string type, CancellationToken ct);

    /// <summary>The account capability object of the given type (account level, else session level), or null.</summary>
    T? AccountCapability<T>(string accountId, string capability)
        where T : class;

    /// <summary>
    /// Opens the event stream (RFC 8620 §7.3) for <paramref name="types"/> with <c>closeafter=no</c> and a ping every
    /// <paramref name="pingSeconds"/>; the caller reads it line by line. A client without push answers a <see cref="TransportError"/>.
    /// </summary>
    Task<Result<Stream>> OpenEventSourceAsync(IReadOnlyList<string> types, int pingSeconds, CancellationToken ct) =>
        Task.FromResult(Result<Stream>.Fail(new TransportError("No push support.", Retryable: false)));
}
