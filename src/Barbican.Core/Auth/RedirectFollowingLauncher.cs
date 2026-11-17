// Mirrors: M1-plan §1 rule 2 and §6.3 ("in AUTOMATION builds a fake that GETs the authorize URL from FakeJmap and delivers the redirect to
// the loopback"); ADR 0007 (hidden automation: never a real browser). Stands in for the user's browser: it asks the authorization endpoint
// through the app's own transport (FakeJmap in automation and tests), takes the redirect it answers with and delivers it to the loopback
// listener. It refuses any redirect that does not point at 127.0.0.1, so it can never be steered to another host.

using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Transport;

namespace Barbican.Core.Auth;

/// <summary>An <see cref="IAuthorizationLauncher"/> that follows the authorization redirect itself (tests and AUTOMATION builds only).</summary>
public sealed class RedirectFollowingLauncher(IJmapHttp http) : IAuthorizationLauncher
{
    /// <summary>The URLs followed, in order (tests read them).</summary>
    public IReadOnlyList<Uri> Opened => _opened;

    private readonly List<Uri> _opened = [];

    /// <inheritdoc/>
    public async Task<Result<Unit>> OpenAsync(Uri authorizeUri, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(authorizeUri);
        lock (_opened)
        {
            _opened.Add(authorizeUri);
        }

        Uri? location;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, authorizeUri);
            using var response = await http.ClientFor(authorizeUri).SendAsync(request, ct).ConfigureAwait(false);
            location = response.Headers.Location;
            if ((int)response.StatusCode is < 300 or >= 400 || location is null)
            {
                return Result<Unit>.Fail(new AuthError($"The authorization endpoint answered {(int)response.StatusCode} without a redirect.", NeedsSignIn: false));
            }
        }
        catch (HttpRequestException e)
        {
            return Result<Unit>.Fail(new TransportError(e.Message, Retryable: false));
        }

        if (!location.IsAbsoluteUri || !string.Equals(location.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) || !IPAddress.TryParse(location.Host, out var ip) || !IPAddress.IsLoopback(ip))
        {
            return Result<Unit>.Fail(new AuthError("The authorization redirect does not point at the loopback listener.", NeedsSignIn: false));
        }

        return await DeliverAsync(location, ct).ConfigureAwait(false);
    }

    /// <summary>A plain HTTP/1.1 GET to the loopback listener (the browser's half of the redirect); the answer is read and dropped.</summary>
    public static async Task<Result<Unit>> DeliverAsync(Uri loopback, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(loopback);
        try
        {
            using var tcp = new TcpClient(AddressFamily.InterNetwork);
            await tcp.ConnectAsync(IPAddress.Loopback, loopback.Port, ct).ConfigureAwait(false);
            var stream = tcp.GetStream();
            var head = string.Create(CultureInfo.InvariantCulture, $"GET {loopback.PathAndQuery} HTTP/1.1\r\nHost: 127.0.0.1:{loopback.Port}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(Encoding.ASCII.GetBytes(head), ct).ConfigureAwait(false);
            var buf = new byte[4096];
            while (await stream.ReadAsync(buf, ct).ConfigureAwait(false) > 0)
            {
                // drain until the listener closes the connection
            }

            return Result<Unit>.Ok(default);
        }
        catch (SocketException e)
        {
            return Result<Unit>.Fail(new TransportError(e.Message, Retryable: false));
        }
        catch (IOException e)
        {
            return Result<Unit>.Fail(new TransportError(e.Message, Retryable: false));
        }
    }
}
