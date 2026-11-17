// Mirrors: Rust jmap.rs:139 open (fetch the session, require a primary mail account, size the gates from the core limits);
// Bulwark lib/jmap/client.ts:2150 connect; RFC 8620 §2.2 (/.well-known/jmap discovery). Owner jmap.

using Barbican.Core.Auth;
using Barbican.Core.Common;

namespace Barbican.Core.Jmap.Transport;

/// <summary>Creates connected <see cref="JmapClient"/>s.</summary>
public sealed class JmapClientFactory(IJmapHttp http, TimeProvider time) : IJmapClientFactory
{
    /// <inheritdoc/>
    public async Task<Result<IJmapClient>> ConnectAsync(ServerEndpoint server, IAccessTokenSource auth, JmapClientOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(auth);
        ArgumentNullException.ThrowIfNull(options);
        var trace = JmapTrace.ToFile(options.TracePath ?? Environment.GetEnvironmentVariable(JmapTrace.Variable));
        var session = await SessionFetcher.FetchAsync(http, auth, server.DiscoveryUri, options.RequestTimeout, time, ct).ConfigureAwait(false);
        trace.Event("SESSION", session.IsOk ? "" : "(failed)");
        return session.IsOk
            ? Result<IJmapClient>.Ok(new JmapClient(http, auth, session.Value!, options, time, trace))
            : Result<IJmapClient>.Fail(session.Error!);
    }
}
