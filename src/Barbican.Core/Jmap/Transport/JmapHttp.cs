// Mirrors: PORT-PLAN.md §3.4 "Transport" (one HttpClient per origin on SocketsHttpHandler, pooled, HTTP/2 where offered); Rust
// jmap.rs:140 (one reqwest client per login), :1113 (the push client's TCP keepalive). Redirects are followed by the client code
// (JmapClientFactory), not the handler, so the Authorization header is kept on same-origin hops only. No cookie store (Fable review 1 F-02):
// one client per origin serves every account on that server, so a cookie set for one login would ride along with another's requests;
// Rust's reqwest client had no cookie store either. Owner jmap.

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

        return new HttpClient(CreateHandler(), disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan,
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };
#pragma warning restore RS0030
    }

    /// <summary>The socket handler of every real client: no redirects, no cookies, pooled HTTP/2.</summary>
    internal static SocketsHttpHandler CreateHandler() =>
        new()
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(30),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            EnableMultipleHttp2Connections = true,
            KeepAlivePingDelay = TimeSpan.FromSeconds(30),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
        };
}
