// Wiring for tests and the headless scenario runner: a JmapClient talking to a FakeJmapServer through the production transport
// (JmapHttp over the fake handler), on a given clock. Owner jmap.

using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Transport;

namespace Barbican.FakeJmap;

/// <summary>Connects the real client to the fake server.</summary>
public static class FakeJmapHarness
{
    /// <summary>A connected client (Basic auth with the fake password); the trace goes to <paramref name="trace"/> when given.</summary>
    public static async Task<JmapClient> ConnectAsync(FakeJmapServer server, TimeProvider time, Action<string>? trace = null, bool readOnly = false, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        var http = new JmapHttp(() => server);
        var auth = StaticAccessTokenSource.Basic(server.Options.Username, server.Options.Password);
        var endpoint = ServerEndpoint.Parse(server.Base) ?? throw new InvalidOperationException("bad base");
        var session = await SessionFetcher.FetchAsync(http, auth, endpoint, endpoint.DiscoveryUri, TimeSpan.FromSeconds(30), time, ct).ConfigureAwait(false);
        var t = trace is null ? JmapTrace.Off : JmapTrace.To(trace);
        t.Event("SESSION", "");
        return session.IsOk
            ? new JmapClient(http, auth, session.Value!, new JmapClientOptions { ReadOnly = readOnly }, time, t)
            : throw new InvalidOperationException(session.Error!.Message);
    }

    /// <summary>The value of a result or an exception with its error.</summary>
    public static T Value<T>(this Result<T> result) => result.IsOk ? result.Value! : throw new InvalidOperationException(result.Error!.Message);
}
