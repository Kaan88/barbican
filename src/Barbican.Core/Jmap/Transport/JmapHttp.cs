// Mirrors: PORT-PLAN.md §3.4 "Transport" (one HttpClient per origin on SocketsHttpHandler, pooled, HTTP/2 where offered); Rust
// jmap.rs:140 (one reqwest client per login), :1113 (the push client's TCP keepalive). Redirects are followed by the client code
// (JmapClientFactory), not the handler, so the Authorization header is kept on same-origin hops only. Owner jmap.

using System.Collections.Concurrent;
using System.Net;

namespace Barbican.Core.Jmap.Transport;

/// <summary>The production <see cref="IJmapHttp"/>, or one over a test handler (FakeJmap).</summary>
public sealed class JmapHttp : IJmapHttp, IDisposable
{
    private readonly ConcurrentDictionary<string, HttpClient> _clients = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<HttpMessageHandler>? _handler;

    /// <summary>Real sockets.</summary>
    public JmapHttp()
    {
    }

    /// <summary>Every origin goes to the handler <paramref name="handler"/> creates (not disposed by this object).</summary>
    public JmapHttp(Func<HttpMessageHandler> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handler = handler;
    }

    /// <inheritdoc/>
    public HttpClient ClientFor(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return _clients.GetOrAdd(uri.GetLeftPart(UriPartial.Authority), _ => Create());
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var c in _clients.Values)
        {
            c.Dispose();
        }

        _clients.Clear();
    }

    private HttpClient Create()
    {
        // RS0030: this is the one place Core creates an HttpClient (eng/BannedSymbols.Core.txt); every request deadline is set per call
        // through a CancellationToken on the injected TimeProvider, so the client's own timeout is off.
#pragma warning disable RS0030
        if (_handler is not null)
        {
            return new HttpClient(_handler(), disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
        }

        var sockets = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(30),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            EnableMultipleHttp2Connections = true,
            KeepAlivePingDelay = TimeSpan.FromSeconds(30),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
        };
        return new HttpClient(sockets, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan,
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };
#pragma warning restore RS0030
    }
}
