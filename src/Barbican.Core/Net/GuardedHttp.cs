// Mirrors: M2 plan §6.3 step 2 and §4.5; Rust reader.rs:169 one_click_post (resolve once, refuse any non-public address, pin the connection to
// the checked address, no redirects, 20 s) and avatars.rs:231 get_capped (a size cap on the body); OWASP SSRF prevention (DNS rebinding:
// the address that was checked is the address that is connected to; the name is never resolved a second time); RFC 6890 via PublicAddress.
// The only outbound HTTP besides the JMAP transport. Everything is decided in the SocketsHttpHandler ConnectCallback, so a pooled
// connection is always one whose address passed the check.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;

namespace Barbican.Core.Net;

/// <summary>The only HTTP client besides the JMAP transport.</summary>
public sealed class GuardedHttp : IGuardedHttp, IDisposable
{
    /// <summary>The body cap: a larger answer is a failure (avatars and logos are far smaller; a one-click answer is ignored).</summary>
    public const int DefaultMaxBodyBytes = 2 * 1024 * 1024;

    /// <summary>The message of a refusal for a private or local address.</summary>
    public const string PrivateNetwork = "The link points into a private network.";

    private readonly bool _loopbackHttp;
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolve;
    private readonly TimeProvider _time;
    private readonly int _maxBodyBytes;
    private readonly TimeSpan _timeout;
    private readonly HttpClient _client;

    /// <summary>The production client: https only, public addresses only, 2 MB, 20 s.</summary>
    public GuardedHttp()
        : this(false, SystemResolveAsync, TimeProvider.System, DefaultMaxBodyBytes, TimeSpan.FromSeconds(20))
    {
    }

    /// <summary>The test seam: <paramref name="loopbackHttp"/> also allows plain http and loopback addresses (still refusing every other private one).</summary>
    internal GuardedHttp(bool loopbackHttp, Func<string, CancellationToken, Task<IPAddress[]>> resolve, TimeProvider time, int maxBodyBytes, TimeSpan timeout)
    {
        _loopbackHttp = loopbackHttp;
        _resolve = resolve;
        _time = time;
        _maxBodyBytes = maxBodyBytes;
        _timeout = timeout;
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false,
            Proxy = null,
            Credentials = null,
            PreAuthenticate = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = timeout,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            MaxResponseHeadersLength = 64,
            ConnectCallback = ConnectAsync,
        };

        // RS0030: GuardedHttp is the one place besides Core/Jmap/Transport that creates an HttpClient (M2 plan §6.3 step 2). Its handler is
        // built right above: every connection passes ConnectAsync's public-address check; no redirect, cookie, credential or proxy.
#pragma warning disable RS0030
        _client = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
#pragma warning restore RS0030
    }

    /// <summary>A client that also allows plain http to loopback, for the tests' own listener on port 0 (never a real network).</summary>
    internal static GuardedHttp ForLoopbackTests() => new(true, SystemResolveAsync, TimeProvider.System, DefaultMaxBodyBytes, TimeSpan.FromSeconds(20));

    /// <inheritdoc />
    public async Task<Result<GuardedResponse>> SendAsync(GuardedRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Check(request) is { } refused)
        {
            return Fail(refused);
        }

        using var deadline = new CancellationTokenSource(_timeout, _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
        using var message = Message(request);
        try
        {
            using var response = await _client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            var type = response.Content.Headers.ContentType?.ToString();
            if (status is >= 300 and < 400)
            {
                return Result<GuardedResponse>.Ok(new GuardedResponse(status, ReadOnlyMemory<byte>.Empty, type));
            }

            if (response.Content.Headers.ContentLength > _maxBodyBytes)
            {
                return Fail("The answer is too large.");
            }

            var body = await ReadCappedAsync(response.Content, linked.Token).ConfigureAwait(false);
            return body is null ? Fail("The answer is too large.") : Result<GuardedResponse>.Ok(new GuardedResponse(status, body, type));
        }
        catch (Exception e) when (Refusal(e) is { } refusal)
        {
            return Fail(refusal.Message);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Result<GuardedResponse>.Fail(new TransportError("The server took too long to answer.", true));
        }
        catch (HttpRequestException e)
        {
            return Result<GuardedResponse>.Fail(new TransportError(e.Message, true));
        }
        catch (IOException e)
        {
            return Result<GuardedResponse>.Fail(new TransportError(e.Message, true));
        }
    }

    /// <inheritdoc />
    public void Dispose() => _client.Dispose();

    /// <summary>Whether an address may be connected to: public, or loopback through the test seam.</summary>
    internal bool Allowed(IPAddress address) => PublicAddress.IsPublic(address) || (_loopbackHttp && IPAddress.IsLoopback(address));

    private static Task<IPAddress[]> SystemResolveAsync(string host, CancellationToken ct) => Dns.GetHostAddressesAsync(host, ct);

    private string? Check(GuardedRequest request)
    {
        var uri = request.Uri;
        var scheme = uri.IsAbsoluteUri ? uri.Scheme : string.Empty;
        if (!(string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) || (_loopbackHttp && string.Equals(scheme, Uri.UriSchemeHttp, StringComparison.Ordinal))))
        {
            return "Only https links are fetched.";
        }

        if (uri.UserInfo.Length > 0)
        {
            return "A link with a user name or password is not fetched.";
        }

        return request.Method != HttpMethod.Get && request.Method != HttpMethod.Post ? "Only GET and POST are sent." : null;
    }

    private static HttpRequestMessage Message(GuardedRequest request)
    {
        var message = new HttpRequestMessage(request.Method, request.Uri) { Version = HttpVersion.Version11, VersionPolicy = HttpVersionPolicy.RequestVersionOrLower };
        message.Headers.UserAgent.ParseAdd("Barbican");
        if (request.Accept is { } accept)
        {
            message.Headers.TryAddWithoutValidation("Accept", accept);
        }

        if (request.FormBody is { } form)
        {
            message.Content = new StringContent(form, Encoding.UTF8);
            message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
        }

        return message;
    }

    // The body, or null once it passes the cap (read in chunks, so a lying or missing Content-Length cannot make it larger).
    private async Task<byte[]?> ReadCappedAsync(HttpContent content, CancellationToken ct)
    {
        var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            while (true)
            {
                var n = await stream.ReadAsync(chunk, ct).ConfigureAwait(false);
                if (n == 0)
                {
                    return buffer.ToArray();
                }

                if (buffer.Length + n > _maxBodyBytes)
                {
                    return null;
                }

                await buffer.WriteAsync(chunk.AsMemory(0, n), ct).ConfigureAwait(false);
            }
        }
    }

    private static Result<GuardedResponse> Fail(string text) => Result<GuardedResponse>.Fail(new ValidationError(text));

    private static GuardedRefusal? Refusal(Exception? e)
    {
        for (; e is not null; e = e.InnerException)
        {
            if (e is GuardedRefusal r)
            {
                return r;
            }
        }

        return null;
    }

    // Resolves once, refuses when any answer is not allowed (a mixed answer is how DNS rebinding starts), then connects to the checked
    // addresses in order; the name is never handed to the socket.
    private async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var host = context.DnsEndPoint.Host.Trim('[', ']');
        var addresses = IPAddress.TryParse(host, out var literal) ? [literal] : await _resolve(host, ct).ConfigureAwait(false);
        if (addresses.Length == 0)
        {
            throw new GuardedRefusal("The link's host name does not resolve.");
        }

        if (addresses.Any(a => !Allowed(a)))
        {
            throw new GuardedRefusal(PrivateNetwork);
        }

        SocketException? last = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), ct).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException e)
            {
                socket.Dispose();
                last = e;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        throw last ?? new SocketException((int)SocketError.HostUnreachable);
    }
}
