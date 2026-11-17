// Mirrors: Bulwark lib/jmap/client.ts connect :2150; Rust jmap.rs fetch_session :73. Contract stub from F2; owner jmap.

using Barbican.Core.Auth;
using Barbican.Core.Common;

namespace Barbican.Core.Jmap;

/// <summary>Creates connected <see cref="IJmapClient"/>s (the only place a client is built, so tests can swap it).</summary>
public interface IJmapClientFactory
{
    /// <summary>Discovers and loads the session, then returns the client.</summary>
    Task<Result<IJmapClient>> ConnectAsync(ServerEndpoint server, IAccessTokenSource auth, JmapClientOptions options, CancellationToken ct);
}
