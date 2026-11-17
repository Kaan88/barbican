// M2 plan §6.3 step 2 exit tests for GuardedHttp: every private range refused, a DNS answer with one private address refused, the connection
// pinned to the checked address (one resolution per connection, so a rebinding answer on the next connection is refused), redirects
// returned and never followed, the size and time caps. Only loopback listeners on port 0; the refusals never open a socket.

using System.Net;
using System.Net.Sockets;
using System.Text;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Net;

namespace Barbican.Core.Tests.Net;

public sealed class GuardedHttpTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.254")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("100.127.255.254")]
    [InlineData("192.0.2.1")]
    [InlineData("198.51.100.7")]
    [InlineData("203.0.113.9")]
    [InlineData("224.0.0.1")]
    [InlineData("240.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456::1")]
    [InlineData("fe80::1")]
    [InlineData("fec0::1")]
    [InlineData("ff02::1")]
    [InlineData("2001:db8::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:10.0.0.1")]
    [InlineData("::10.0.0.1")]
    [InlineData("64:ff9b::a00:1")]
    [InlineData("2002:a00:1::1")]
    [InlineData("192.0.0.8")]
    [InlineData("198.18.0.1")]
    [InlineData("198.19.255.254")]
    [InlineData("64:ff9b:1::a00:1")]
    [InlineData("2001:0:4136:e378:8000:63bf:3fff:fdd2")]
    public async Task Every_private_range_is_refused(string ip)
    {
        Assert.False(PublicAddress.IsPublic(IPAddress.Parse(ip)), ip);
        using var http = new GuardedHttp();
        var host = ip.Contains(':', StringComparison.Ordinal) ? $"[{ip}]" : ip;
        var r = await http.SendAsync(new GuardedRequest(HttpMethod.Get, new Uri($"https://{host}/x")), Ct);
        Assert.False(r.IsOk, ip);
        Assert.Equal(GuardedHttp.PrivateNetwork, r.Error!.Message);
    }

    [Theory]
    [InlineData("93.184.216.34")]
    [InlineData("1.1.1.1")]
    [InlineData("100.128.0.1")]
    [InlineData("172.32.0.1")]
    [InlineData("198.20.0.1")]
    [InlineData("2001:4860:4860::8888")]
    [InlineData("2606:4700::1111")]
    [InlineData("64:ff9b::5db8:d822")]
    [InlineData("::ffff:93.184.216.34")]
    public void Public_addresses_pass(string ip) => Assert.True(PublicAddress.IsPublic(IPAddress.Parse(ip)), ip);

    [Fact]
    public async Task Production_refuses_http_credentials_and_other_methods_before_connecting()
    {
        using var http = new GuardedHttp();
        Assert.Equal("Only https links are fetched.", (await http.SendAsync(new GuardedRequest(HttpMethod.Get, new Uri("http://93.184.216.34/")), Ct)).Error!.Message);
        Assert.Equal("Only https links are fetched.", (await http.SendAsync(new GuardedRequest(HttpMethod.Get, new Uri("ftp://93.184.216.34/")), Ct)).Error!.Message);
        Assert.Contains("user name", (await http.SendAsync(new GuardedRequest(HttpMethod.Get, new Uri("https://u:p@93.184.216.34/")), Ct)).Error!.Message, StringComparison.Ordinal);
        Assert.Contains("GET and POST", (await http.SendAsync(new GuardedRequest(HttpMethod.Put, new Uri("https://93.184.216.34/")), Ct)).Error!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_dns_answer_with_one_private_address_is_refused_without_connecting()
    {
        using var listener = Listen(out var port);
        var resolved = 0;
        using var http = Seam((_, _) =>
        {
            resolved++;
            return Task.FromResult(new[] { IPAddress.Loopback, IPAddress.Parse("10.0.0.1") });
        });
        var r = await http.SendAsync(new GuardedRequest(HttpMethod.Get, new Uri($"http://mixed.example:{port}/x")), Ct);
        Assert.False(r.IsOk);
        Assert.Equal(GuardedHttp.PrivateNetwork, r.Error!.Message);
        Assert.Equal(1, resolved);
        Assert.False(listener.Pending(), "no connection was opened");
    }

    [Fact]
    public async Task The_connection_goes_to_the_checked_address_and_a_rebinding_answer_is_refused()
    {
        using var listener = Listen(out var port);
        var answers = new Queue<IPAddress[]>([[IPAddress.Loopback], [IPAddress.Parse("192.168.0.10")]]);
        var asked = new List<string>();
        using var http = Seam((host, _) =>
        {
            asked.Add(host);
            return Task.FromResult(answers.Dequeue());
        });
        var server = AnswerOnceAsync(listener, "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok");
        var first = await http.SendAsync(new GuardedRequest(HttpMethod.Get, new Uri($"http://pinned.example:{port}/a")), Ct);
        Assert.True(first.IsOk, first.Error?.Message);
        Assert.Equal("ok", Encoding.ASCII.GetString(first.Value!.Body.Span));
        var request = await server;
        Assert.Contains($"Host: pinned.example:{port}", request, StringComparison.Ordinal);
        Assert.DoesNotContain("cookie", request.ToLowerInvariant(), StringComparison.Ordinal);

        // the server closed the connection, so the next request resolves again: the new answer is private and refused
        var second = await http.SendAsync(new GuardedRequest(HttpMethod.Get, new Uri($"http://pinned.example:{port}/b")), Ct);
        Assert.False(second.IsOk);
        Assert.Equal(GuardedHttp.PrivateNetwork, second.Error!.Message);
        Assert.Equal(["pinned.example", "pinned.example"], asked);
    }

    [Fact]
    public async Task A_redirect_is_returned_and_not_followed()
    {
        using var listener = Listen(out var port);
        using var http = Seam(LoopbackAsync);
        var server = AnswerOnceAsync(listener, $"HTTP/1.1 302 Found\r\nLocation: http://127.0.0.1:{port}/next\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        var r = await http.SendAsync(new GuardedRequest(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/x")), Ct);
        await server;
        Assert.True(r.IsOk);
        Assert.Equal(302, r.Value!.StatusCode);
        Assert.False(listener.Pending(), "the Location was not requested");
    }

    [Fact]
    public async Task A_body_over_the_cap_fails_with_or_without_a_length()
    {
        using var listener = Listen(out var port);
        using var http = Seam(LoopbackAsync, maxBodyBytes: 1000);
        var big = new string('x', 5000);
        var withLength = AnswerOnceAsync(listener, $"HTTP/1.1 200 OK\r\nContent-Length: {big.Length}\r\nConnection: close\r\n\r\n{big}");
        var a = await http.SendAsync(new GuardedRequest(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/a")), Ct);
        await withLength;
        Assert.Equal("The answer is too large.", a.Error!.Message);

        var noLength = AnswerOnceAsync(listener, $"HTTP/1.1 200 OK\r\nConnection: close\r\n\r\n{big}");
        var b = await http.SendAsync(new GuardedRequest(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/b")), Ct);
        await noLength;
        Assert.Equal("The answer is too large.", b.Error!.Message);
    }

    [Fact]
    public async Task A_server_that_never_answers_runs_into_the_time_cap()
    {
        using var listener = Listen(out var port);
        using var http = Seam(LoopbackAsync, timeout: TimeSpan.FromMilliseconds(300));
        var accepted = Task.Run(async () => await listener.AcceptTcpClientAsync(Ct), Ct);
        var r = await http.SendAsync(new GuardedRequest(HttpMethod.Get, new Uri($"http://127.0.0.1:{port}/slow")), Ct);
        Assert.False(r.IsOk);
        Assert.IsType<TransportError>(r.Error);
        Assert.Contains("too long", r.Error!.Message, StringComparison.Ordinal);
        (await accepted).Dispose();
    }

    private static Task<IPAddress[]> LoopbackAsync(string host, CancellationToken ct) => Task.FromResult(new[] { IPAddress.Loopback });

    private static GuardedHttp Seam(Func<string, CancellationToken, Task<IPAddress[]>> resolve, int maxBodyBytes = GuardedHttp.DefaultMaxBodyBytes, TimeSpan? timeout = null) =>
        new(true, resolve, TimeProvider.System, maxBodyBytes, timeout ?? TimeSpan.FromSeconds(10));

    private static TcpListener Listen(out int port)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        port = ((IPEndPoint)listener.LocalEndpoint).Port;
        return listener;
    }

    // Accepts one connection, reads the request head and answers with the given raw bytes.
    private static Task<string> AnswerOnceAsync(TcpListener listener, string answer) => Task.Run(
        async () =>
        {
            using var client = await listener.AcceptTcpClientAsync(Ct);
            var stream = client.GetStream();
            var buf = new byte[8192];
            var got = new StringBuilder();
            while (!got.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                var n = await stream.ReadAsync(buf, Ct);
                if (n == 0)
                {
                    break;
                }

                got.Append(Encoding.ASCII.GetString(buf, 0, n));
            }

            await stream.WriteAsync(Encoding.ASCII.GetBytes(answer), Ct);
            return got.ToString();
        },
        Ct);
}
