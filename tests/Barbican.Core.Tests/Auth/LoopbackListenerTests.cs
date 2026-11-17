// The loopback redirect of RFC 8252 §7.3 and §8.9 (state). Rust oauth.rs:150 ended the sign-in on the first request without the right state;
// this one ignores such requests (FABLE #3) and accepts only the first callback with it.

using System.Net.Sockets;
using System.Text;
using Barbican.Core.Auth;
using Barbican.Core.Jmap.Errors;

namespace Barbican.Core.Tests.Auth;

public sealed class LoopbackListenerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<string> GetAsync(int port, string pathAndQuery)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync("127.0.0.1", port, Ct);
        var s = tcp.GetStream();
        await s.WriteAsync(Encoding.ASCII.GetBytes($"GET {pathAndQuery} HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: close\r\n\r\n"), Ct);
        using var reader = new StreamReader(s, Encoding.ASCII);
        return await reader.ReadLineAsync(Ct) ?? "";
    }

    [Fact]
    public async Task A_wrong_or_missing_state_is_refused_and_the_wait_goes_on()
    {
        using var l = LoopbackListener.Start(0).Value!;
        var wait = l.WaitForCodeAsync("good-state", TimeSpan.FromSeconds(10), TimeProvider.System, Ct);
        Assert.Contains("404", await GetAsync(l.Port, "/favicon.ico"), StringComparison.Ordinal);
        Assert.Contains("400", await GetAsync(l.Port, "/callback?code=evil&state=bad-state"), StringComparison.Ordinal);
        Assert.Contains("400", await GetAsync(l.Port, "/callback?code=evil"), StringComparison.Ordinal);
        Assert.Contains("400", await GetAsync(l.Port, "/callback?error=access_denied&state=bad"), StringComparison.Ordinal);
        Assert.False(wait.IsCompleted);
        Assert.Contains("200", await GetAsync(l.Port, "/callback?code=the-code&state=good-state"), StringComparison.Ordinal);
        var r = await wait;
        Assert.True(r.IsOk);
        Assert.Equal("the-code", r.Value);
        Assert.Equal(3, l.Refused);
    }

    [Fact]
    public async Task A_second_callback_is_not_accepted()
    {
        using var l = LoopbackListener.Start(0).Value!;
        var wait = l.WaitForCodeAsync("s", TimeSpan.FromSeconds(10), TimeProvider.System, Ct);
        await GetAsync(l.Port, "/callback?code=first&state=s");
        Assert.Equal("first", (await wait).Value);

        // the listener is closed after the first: a replay finds nobody listening
        await Assert.ThrowsAnyAsync<SocketException>(() => GetAsync(l.Port, "/callback?code=second&state=s"));
    }

    [Fact]
    public async Task An_error_with_the_right_state_ends_the_sign_in()
    {
        using var l = LoopbackListener.Start(0).Value!;
        var wait = l.WaitForCodeAsync("s", TimeSpan.FromSeconds(10), TimeProvider.System, Ct);
        await GetAsync(l.Port, "/callback?error=access_denied&error_description=No%20thanks&state=s");
        var r = await wait;
        var e = Assert.IsType<AuthError>(r.Error);
        Assert.StartsWith("The provider refused the sign-in", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_wait_times_out()
    {
        using var l = LoopbackListener.Start(0).Value!;
        var r = await l.WaitForCodeAsync("s", TimeSpan.FromMilliseconds(200), TimeProvider.System, Ct);
        Assert.Equal("Sign-in timed out.", r.Error!.Message);
    }

    [Fact]
    public async Task The_wait_times_out_while_a_silent_connection_is_open()
    {
        using var l = LoopbackListener.Start(0).Value!;
        var wait = l.WaitForCodeAsync("s", TimeSpan.FromMilliseconds(300), TimeProvider.System, Ct);

        // a client that connects and never sends a request line: the deadline passes during the read, not during accept
        using var silent = new TcpClient();
        await silent.ConnectAsync("127.0.0.1", l.Port, Ct);
        var r = await wait;
        Assert.Equal("Sign-in timed out.", r.Error!.Message);
    }

    [Fact]
    public void The_redirect_is_on_127_0_0_1()
    {
        using var l = LoopbackListener.Start(0).Value!;
        Assert.Equal("127.0.0.1", l.RedirectUri.Host);
        Assert.Equal("/callback", l.RedirectUri.AbsolutePath);
    }

    [Fact]
    public void Query_parsing_decodes()
    {
        var q = LoopbackListener.ParseQuery("?code=a%2Bb&state=x+y&empty=");
        Assert.Equal("a+b", q["code"]);
        Assert.Equal("x y", q["state"]);
        Assert.Equal("", q["empty"]);
    }
}
