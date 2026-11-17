// Ported from Rust src/reader.rs tests one_click_refuses_private_networks (:1296), one_click_does_not_follow_redirects (:1309),
// one_click_posts_the_form_body (:1335), plus public_ip. The listener tests use a loopback listener on port 0 only, through the GuardedHttp
// test seam (M2 plan 2.1: T-216..T-218 never touch a real network).

using System.Net;
using System.Net.Sockets;
using System.Text;
using Barbican.Core.Net;

namespace Barbican.Core.Tests.Net;

public sealed class OneClickUnsubscribeTests
{
    [Fact]
    [RustTest("reader::one_click_refuses_private_networks")]
    public async Task One_click_refuses_private_networks()
    {
        var http = new GuardedHttp();
        foreach (var u in new[] { "https://127.0.0.1/u", "https://localhost/u", "https://10.1.2.3/u", "https://192.168.1.1/u", "https://[::1]/u", "https://169.254.169.254/latest" })
        {
            var r = await OneClickUnsubscribe.PostAsync(http, u, TestContext.Current.CancellationToken);
            Assert.False(r.IsOk, u);
        }

        Assert.True(PublicAddress.IsPublic(IPAddress.Parse("93.184.216.34")));
        Assert.False(PublicAddress.IsPublic(IPAddress.Parse("100.64.0.1")));
        Assert.False(PublicAddress.IsPublic(IPAddress.Parse("::ffff:127.0.0.1")));
        Assert.False(PublicAddress.IsPublic(IPAddress.Parse("fd00::1")));
    }

    [Fact]
    [RustTest("reader::one_click_does_not_follow_redirects")]
    public async Task One_click_does_not_follow_redirects()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
            var stream = client.GetStream();
            var buf = new byte[4096];
            _ = await stream.ReadAsync(buf);
            await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 302 Found\r\nLocation: http://127.0.0.1:1/x\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"));
        }, TestContext.Current.CancellationToken);

        var r = await OneClickUnsubscribe.PostFormAsync(GuardedHttp.ForLoopbackTests(), $"http://127.0.0.1:{port}/u", TestContext.Current.CancellationToken);
        Assert.False(r.IsOk);
        Assert.Contains("redirected", r.Error!.Message, StringComparison.Ordinal);
        await server;
    }

    [Fact]
    [RustTest("reader::one_click_posts_the_form_body")]
    public async Task One_click_posts_the_form_body()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
            var stream = client.GetStream();
            var buf = new byte[4096];
            var got = new StringBuilder();
            while (!got.ToString().Contains("One-Click", StringComparison.Ordinal))
            {
                var n = await stream.ReadAsync(buf);
                if (n == 0)
                {
                    break;
                }

                got.Append(Encoding.UTF8.GetString(buf, 0, n));
            }

            await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"));
            return got.ToString();
        }, TestContext.Current.CancellationToken);

        var r = await OneClickUnsubscribe.PostFormAsync(GuardedHttp.ForLoopbackTests(), $"http://127.0.0.1:{port}/unsub?u=1", TestContext.Current.CancellationToken);
        Assert.True(r.IsOk);
        var req = await server;
        Assert.StartsWith("POST /unsub?u=1 HTTP/1.1", req, StringComparison.Ordinal);
        Assert.Contains("content-type: application/x-www-form-urlencoded", req.ToLowerInvariant(), StringComparison.Ordinal);
        Assert.EndsWith("List-Unsubscribe=One-Click", req, StringComparison.Ordinal);
        Assert.DoesNotContain("cookie", req.ToLowerInvariant(), StringComparison.Ordinal);
        Assert.DoesNotContain("authorization", req.ToLowerInvariant(), StringComparison.Ordinal);
    }
}
