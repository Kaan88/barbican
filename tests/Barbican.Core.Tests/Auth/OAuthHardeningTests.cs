// Fable review 1 F-04: the loopback accepts GET /callback only and keeps waiting on anything else, checks RFC 9207 iss after state, and
// discovery refuses an external provider's endpoints unless they are https (http on this computer only, D-10). Owner accounts
// (fix-security stream).

using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using Barbican.Core.Auth;
using Barbican.Core.Jmap.Errors;

namespace Barbican.Core.Tests.Auth;

public sealed class OAuthHardeningTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<string> RequestAsync(int port, string method, string pathAndQuery)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync("127.0.0.1", port, Ct);
        var s = tcp.GetStream();
        await s.WriteAsync(Encoding.ASCII.GetBytes($"{method} {pathAndQuery} HTTP/1.1\r\nHost: 127.0.0.1\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), Ct);
        using var reader = new StreamReader(s, Encoding.ASCII);
        return await reader.ReadLineAsync(Ct) ?? "";
    }

    [Fact]
    public async Task Only_a_GET_is_a_callback_and_anything_else_leaves_the_wait_running()
    {
        using var l = LoopbackListener.Start(0).Value!;
        var wait = l.WaitForCodeAsync(new CallbackCheck("s", null, false), TimeSpan.FromSeconds(10), TimeProvider.System, Ct);
        Assert.Contains("405", await RequestAsync(l.Port, "POST", "/callback?code=evil&state=s"), StringComparison.Ordinal);
        Assert.Contains("405", await RequestAsync(l.Port, "OPTIONS", "/callback?code=evil&state=s"), StringComparison.Ordinal);
        Assert.Contains("400", await RequestAsync(l.Port, "GET", "/callback?error=access_denied"), StringComparison.Ordinal);
        Assert.False(wait.IsCompleted);
        Assert.Contains("200", await RequestAsync(l.Port, "GET", "/callback?code=good&state=s"), StringComparison.Ordinal);
        Assert.Equal("good", (await wait).Value);
        Assert.Equal(3, l.Refused);
    }

    [Theory]
    [InlineData("https://idp.example.org", false, "&iss=https%3A%2F%2Fidp.example.org", true)]
    [InlineData("https://idp.example.org", false, "", true)]
    [InlineData("https://idp.example.org", false, "&iss=https%3A%2F%2Fevil.example.net", false)]
    [InlineData("https://idp.example.org", true, "", false)]
    [InlineData("https://idp.example.org", true, "&iss=https%3A%2F%2Fidp.example.org", true)]
    [InlineData(null, false, "&iss=https%3A%2F%2Fanything.example", true)]
    // M1 hotfix (sign-in): an issuer the metadata does not name cannot be compared; Bulwark browser-sign-in.ts:170 accepts, and so does the app now
    [InlineData(null, true, "&iss=https%3A%2F%2Fanything.example", true)]
    [InlineData(null, true, "", false)]
    public async Task The_iss_of_RFC_9207_is_checked_after_the_state(string? issuer, bool required, string iss, bool accepted)
    {
        using var l = LoopbackListener.Start(0).Value!;
        var wait = l.WaitForCodeAsync(new CallbackCheck("s", issuer, required), TimeSpan.FromSeconds(10), TimeProvider.System, Ct);

        // a wrong iss without the state proves nothing and is refused like any stranger: the wait goes on
        Assert.Contains("400", await RequestAsync(l.Port, "GET", "/callback?code=x&iss=https%3A%2F%2Fevil.example.net"), StringComparison.Ordinal);
        Assert.False(wait.IsCompleted);
        await RequestAsync(l.Port, "GET", "/callback?code=the-code&state=s" + iss);
        var r = await wait;
        Assert.Equal(accepted, r.IsOk);
        if (!accepted)
        {
            Assert.Contains("RFC 9207", Assert.IsType<AuthError>(r.Error).Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void An_external_provider_must_use_https()
    {
        var baseUri = new Uri("https://mail.example.org");
        var plain = new JsonObject { ["authorization_endpoint"] = "http://idp.example.net/auth", ["token_endpoint"] = "https://idp.example.net/token" };
        Assert.Null(OAuthDiscovery.MetaFrom(plain, baseUri, own: false));

        var secure = new JsonObject
        {
            ["issuer"] = "https://idp.example.net",
            ["authorization_endpoint"] = "https://idp.example.net/auth",
            ["token_endpoint"] = "https://idp.example.net/token",
            ["registration_endpoint"] = "http://idp.example.net/register",
            ["authorization_response_iss_parameter_supported"] = true,
        };
        var m = OAuthDiscovery.MetaFrom(secure, baseUri, own: false)!;
        Assert.Equal("https://idp.example.net", m.Issuer);
        Assert.True(m.IssParameterSupported);

        // a plain http registration endpoint is dropped (the flow then uses the fallback client id), not followed
        Assert.Null(m.Registration);

        // a provider on this computer may use plain http (D-10)
        var local = new JsonObject { ["authorization_endpoint"] = "http://127.0.0.1:9000/auth", ["token_endpoint"] = "http://localhost:9000/token" };
        Assert.NotNull(OAuthDiscovery.MetaFrom(local, baseUri, own: false));
    }

    [Fact]
    public void The_servers_own_endpoints_follow_the_typed_origin()
    {
        var own = new JsonObject { ["authorization_endpoint"] = "http://stalwart.internal/auth", ["token_endpoint"] = "/token" };
        var m = OAuthDiscovery.MetaFrom(own, new Uri("https://mail.example.org"), own: true)!;
        Assert.Equal("https://mail.example.org/auth", m.Authorization.ToString());
        Assert.False(m.IssParameterSupported);
        Assert.Null(m.Issuer);
    }
}
