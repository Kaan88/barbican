// Mirrors: PORT-PLAN.md §3.4 "Transport" (one HttpClient per server origin, on SocketsHttpHandler; the only place an HttpClient is
// created) and M1-plan §6.1 (FakeJmap is an HttpMessageHandler, so tests swap the handler, not the client). Owner jmap.

namespace Barbican.Core.Jmap.Transport;

/// <summary>Hands out the shared <see cref="HttpClient"/> of a server origin.</summary>
public interface IJmapHttp
{
    /// <summary>The client for the origin of <paramref name="uri"/> (scheme, host and port).</summary>
    HttpClient ClientFor(Uri uri);
}
