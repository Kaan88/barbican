using System.Net;
using System.Net.Sockets;
using System.Text;
using Barbican.Automation.Protocol;

namespace Barbican.Automation.Tests;

/// <summary>
/// Fable review 1 F-08: an automation connection runs nothing until its first line carries the run's token, and a line that looks like
/// HTTP (a web page POSTing to the loopback port) ends it.
/// </summary>
public sealed class AutomationConnectionTests
{
    private const string Token = "0123456789abcdef0123456789abcdef";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(List<string> Replies, List<string> Ran)> TalkAsync(string send)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var ran = new List<string>();
        var serve = Task.Run(async () =>
        {
            using var server = await listener.AcceptTcpClientAsync(Ct);
            await AutomationConnection.ServeAsync(server.GetStream(), Token, (line, _) =>
            {
                ran.Add(line);
                return Task.FromResult("ok " + line);
            }, Ct);
        }, Ct);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port, Ct);
        var stream = client.GetStream();
        await stream.WriteAsync(Encoding.UTF8.GetBytes(send), Ct);
        client.Client.Shutdown(SocketShutdown.Send);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var replies = new List<string>();
        while (await reader.ReadLineAsync(Ct) is { } line)
        {
            replies.Add(line);
        }

        await serve;
        return (replies, ran);
    }

    [Fact]
    public async Task The_token_opens_the_connection()
    {
        var (replies, ran) = await TalkAsync($"auth {Token}\nsize\nfocus\n");
        Assert.Equal(["ok auth", "ok size", "ok focus"], replies);
        Assert.Equal(["size", "focus"], ran);
    }

    [Theory]
    [InlineData("size\nauth " + Token + "\nsize\n")]
    [InlineData("auth wrong-token-of-some-length\nsize\n")]
    [InlineData("auth \nsize\n")]
    [InlineData("POST / HTTP/1.1\r\nHost: 127.0.0.1\r\nContent-Type: text/plain\r\n\r\nwv-eval document.title\n")]
    public async Task Without_the_token_nothing_runs(string send)
    {
        var (replies, ran) = await TalkAsync(send);
        Assert.Equal(["error auth required"], replies);
        Assert.Empty(ran);
    }

    [Fact]
    public async Task A_line_that_looks_like_http_ends_an_open_connection()
    {
        var (replies, ran) = await TalkAsync($"auth {Token}\nsize\nPOST /x HTTP/1.1\nshot C:/x.png\n");
        Assert.Equal(["ok auth", "ok size"], replies);
        Assert.Equal(["size"], ran);
    }

    [Theory]
    [InlineData("GET / HTTP/1.1", true)]
    [InlineData("OPTIONS * HTTP/1.0", true)]
    [InlineData("Host: 127.0.0.1:4242", true)]
    [InlineData("content-length: 12", true)]
    [InlineData("type Hello: world", false)]
    [InlineData("wv-eval document.title", false)]
    [InlineData("click 10 20", false)]
    public void Http_lines_are_told_from_commands(string line, bool http) => Assert.Equal(http, AutomationConnection.LooksLikeHttp(line));

    [Fact]
    public void A_short_or_missing_token_keeps_the_port_closed()
    {
        Assert.Null(AutomationConnection.Usable(null));
        Assert.Null(AutomationConnection.Usable("short"));
        Assert.Null(AutomationConnection.Usable("has a space in it 1234"));
        Assert.Equal(Token, AutomationConnection.Usable(Token));
        Assert.True(AutomationConnection.Authenticates("auth " + Token + "\r", Token));
        Assert.False(AutomationConnection.Authenticates("auth " + Token + "x", Token));
    }
}
