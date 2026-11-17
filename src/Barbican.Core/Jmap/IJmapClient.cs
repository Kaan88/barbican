// Mirrors: Bulwark lib/jmap/client.ts (connect :2150, request :2384, getBlobDownloadUrl :6876); Rust jmap.rs Client;
// M1-plan §4.1. Contract stub from F2; owner jmap.

using Barbican.Core.Common;

namespace Barbican.Core.Jmap;

/// <summary>One connection to a JMAP server for one account (session, requests, downloads).</summary>
public interface IJmapClient
{
    /// <summary>The live session; refreshed when <c>sessionState</c> changes.</summary>
    JmapSession Session { get; }

    /// <summary>True for the read-only account: anything outside the allow-list returns a ReadOnlyViolation.</summary>
    bool IsReadOnly { get; }

    /// <summary>Sends one JMAP request (split by <c>maxCallsInRequest</c> unless the calls are linked).</summary>
    Task<Result<JmapResponse>> SendAsync(JmapRequest request, CancellationToken ct);

    /// <summary>Downloads a blob through the session's download URL template.</summary>
    Task<Result<Stream>> DownloadAsync(string accountId, string blobId, string name, string type, CancellationToken ct);

    /// <summary>The account capability object of the given type, or null.</summary>
    T? AccountCapability<T>(string accountId, string capability)
        where T : class;
}
