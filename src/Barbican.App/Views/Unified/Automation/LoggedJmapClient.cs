#if AUTOMATION
// Mirrors: M2 plan 6.5 hidden scenario ("check the request log": a per-account request log). Owner unified. Passes every call to the inner
// client and reports the method names of each request it sends.

using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Transport;

namespace Barbican.App.Views.Unified.Automation;

/// <summary>A client that reports the methods of each request it sends.</summary>
public sealed class LoggedJmapClient(IJmapClient inner, Action<string> log) : IJmapClient
{
    public JmapSession Session => inner.Session;

    public bool IsReadOnly => inner.IsReadOnly;

    public JmapTrace Trace => inner.Trace;

    public Task<Result<JmapResponse>> SendAsync(JmapRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        log(string.Join(",", request.MethodCalls.Select(c => (string?)c[0] ?? "?")));
        return inner.SendAsync(request, ct);
    }

    public Task<Result<Stream>> DownloadAsync(string accountId, string blobId, string name, string type, CancellationToken ct) =>
        inner.DownloadAsync(accountId, blobId, name, type, ct);

    public T? AccountCapability<T>(string accountId, string capability)
        where T : class => inner.AccountCapability<T>(accountId, capability);

    public Task<Result<Stream>> OpenEventSourceAsync(IReadOnlyList<string> types, int pingSeconds, CancellationToken ct) =>
        inner.OpenEventSourceAsync(types, pingSeconds, ct);
}
#endif
