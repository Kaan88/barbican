// Fable review 1 F-02 and user decision D-10: the URLs of a session move onto the origin the user typed (Bulwark lib/jmap/client.ts:2326-2362),
// a session read over https names no http URL, credentials go only to the typed origin and the https hosts the session keeps, a session that
// came from another origin keeps nothing off the typed one, the shared clients keep no cookies, and plain http is for this computer only.
// Owner jmap (fix-security stream).

using System.Text.Json.Nodes;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Transport;
using Barbican.FakeJmap;

namespace Barbican.Core.Tests.Jmap;

public sealed class SessionUrlTests
{
    private static readonly ServerEndpoint Typed = ServerEndpoint.Parse("https://mail.example.org")!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Doc(string api, string download, string? upload = null, string? events = null)
    {
        var o = new JsonObject
        {
            ["accounts"] = new JsonObject { ["a"] = new JsonObject() },
            ["primaryAccounts"] = new JsonObject { [JmapCapabilities.Mail] = "a" },
            ["apiUrl"] = api,
            ["downloadUrl"] = download,
        };
        if (upload is not null)
        {
            o["uploadUrl"] = upload;
        }

        if (events is not null)
        {
            o["eventSourceUrl"] = events;
        }

        return o.ToJsonString();
    }

    private static JmapSession Parse(string json, bool viaOtherOrigin = false, string sessionUrl = "https://mail.example.org/.well-known/jmap") =>
        SessionParser.Parse(json, new Uri(sessionUrl), Typed, viaOtherOrigin).Value!;

    [Fact]
    [Rfc("J-002", "RFC 8620 2")]
    public void An_api_url_on_another_host_moves_onto_the_typed_origin()
    {
        var s = Parse(Doc("https://evil.example.net/jmap/", "https://evil.example.net/jmap/download/{accountId}/{blobId}/{name}?accept={type}"));
        Assert.Equal("https://mail.example.org/jmap/", s.ApiUrl);

        // the download host is the api host as reported, so it is not a host of its own: it moves too
        Assert.Equal("https://mail.example.org/jmap/download/{accountId}/{blobId}/{name}?accept={type}", s.DownloadUrl);
        Assert.Equal(["https://mail.example.org"], s.CredentialOrigins);
        Assert.False(s.AllowsCredentials(new Uri("https://evil.example.net/jmap/")));
        Assert.True(s.AllowsCredentials(new Uri("https://mail.example.org/jmap/")));
    }

    [Fact]
    [Rfc("J-002", "RFC 8620 2")]
    public void An_internal_host_name_behind_a_proxy_is_replaced()
    {
        var s = Parse(Doc("http://stalwart.internal:8080/jmap/", "http://stalwart.internal:8080/jmap/download/{accountId}/{blobId}/{name}?accept={type}",
            "http://stalwart.internal:8080/jmap/upload/{accountId}/", "http://stalwart.internal:8080/jmap/eventsource/?types={types}&closeafter={closeafter}&ping={ping}"));
        Assert.Equal("https://mail.example.org/jmap/", s.ApiUrl);
        Assert.StartsWith("https://mail.example.org/jmap/download/", s.DownloadUrl, StringComparison.Ordinal);
        Assert.Equal("https://mail.example.org/jmap/upload/{accountId}/", s.UploadUrl);
        Assert.StartsWith("https://mail.example.org/jmap/eventsource/?types={types}", s.EventSourceUrl, StringComparison.Ordinal);
        Assert.Equal(["https://mail.example.org"], s.CredentialOrigins);
    }

    [Fact]
    [Rfc("J-002", "RFC 8620 2")]
    public void A_separate_https_download_host_is_kept_and_may_receive_credentials()
    {
        var s = Parse(Doc("https://api.example.org/jmap/", "https://dl.example.net/{accountId}/{blobId}/{name}?type={type}", events: "https://push.example.net/es"));
        Assert.Equal("https://mail.example.org/jmap/", s.ApiUrl);
        Assert.Equal("https://dl.example.net/{accountId}/{blobId}/{name}?type={type}", s.DownloadUrl);
        Assert.Equal("https://push.example.net/es", s.EventSourceUrl);
        Assert.Equal(["https://mail.example.org", "https://dl.example.net", "https://push.example.net"], s.CredentialOrigins);
        Assert.True(s.AllowsCredentials(new Uri("https://dl.example.net/a/b/c?type=x")));
        Assert.False(s.AllowsCredentials(new Uri("https://other.example.net/")));
        Assert.False(s.AllowsCredentials(new Uri("https://user:pw@dl.example.net/a")));
    }

    [Fact]
    [Rfc("J-002", "RFC 8620 2")]
    public void An_http_download_host_is_not_kept_and_moves_onto_https()
    {
        var s = Parse(Doc("https://api.example.org/jmap/", "http://dl.example.net/{blobId}"));
        Assert.Equal("https://mail.example.org/{blobId}", s.DownloadUrl);
        Assert.Equal(["https://mail.example.org"], s.CredentialOrigins);
    }

    [Fact]
    [Rfc("J-002", "RFC 8620 2")]
    public void A_session_from_another_origin_keeps_nothing_off_the_typed_origin()
    {
        var s = Parse(Doc("https://api.example.org/jmap/", "https://dl.example.net/{blobId}"), viaOtherOrigin: true, sessionUrl: "https://other.example.net/jmap/session");
        Assert.Equal("https://mail.example.org/jmap/", s.ApiUrl);
        Assert.Equal("https://mail.example.org/{blobId}", s.DownloadUrl);
        Assert.Equal(["https://mail.example.org"], s.CredentialOrigins);
    }

    [Fact]
    [Rfc("J-002", "RFC 8620 2")]
    public void A_session_read_over_https_may_not_name_http_urls()
    {
        // only a loopback server may be typed with http; its session must not arrive over https and then send the password in clear
        var loopback = ServerEndpoint.Parse("http://127.0.0.1:8080")!;
        var r = SessionParser.Parse(Doc("/jmap/", "/d/{blobId}"), new Uri("https://127.0.0.1:8443/.well-known/jmap"), loopback, viaOtherOrigin: false);
        Assert.False(r.IsOk);
        Assert.Contains("insecure", r.Error!.Message, StringComparison.Ordinal);

        // the same server read over plain http is fine (D-10: this computer only)
        var ok = SessionParser.Parse(Doc("/jmap/", "/d/{blobId}"), new Uri("http://127.0.0.1:8080/.well-known/jmap"), loopback, viaOtherOrigin: false);
        Assert.Equal("http://127.0.0.1:8080/jmap/", ok.Value!.ApiUrl);
    }

    [Fact]
    [Rfc("J-001", "RFC 8620 2.2")]
    public async Task A_proxy_session_naming_its_internal_host_still_works_end_to_end()
    {
        // the fake server refuses any host but its own: every request reaching it proves the rewrite
        var f = await Fake.StartAsync(new FakeJmapOptions { SessionUrlOrigin = "https://stalwart.internal.example:8443" });
        Assert.Equal("https://jmap.example.org/jmap/", f.Client.Session.ApiUrl);
        Assert.True((await f.Client.SendAsync(JmapRequest.Of(JmapRequest.Call("Core/echo", new JsonObject(), "e")), Ct)).IsOk);
        var blob = f.Server.Store.Email(f.Server.Store.EmailIds()[0])!["blobId"]!.GetValue<string>();
        await using var s = (await f.Client.DownloadAsync("b", blob, "m.eml", "message/rfc822", Ct)).Value!;
        Assert.True(s.CanRead);
    }

    [Fact]
    [Rfc("J-001", "RFC 8620 2.2")]
    public async Task A_cross_origin_redirect_says_where_the_server_is_instead_of_wrong_password()
    {
        var server = new FakeJmapServer(new FakeJmapOptions { RedirectWellKnown = true, RedirectOrigin = "https://other.example.org" });
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => FakeJmapHarness.ConnectAsync(server, TimeProvider.System, ct: Ct));
        Assert.Contains("GET /jmap/session 401", server.Requests, StringComparer.Ordinal);
        Assert.Contains("Enter https://other.example.org as the server address", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Rfc("J-020", "RFC 6750 3.1")]
    public void Credentials_are_refused_before_the_token_source_is_asked()
    {
        var r = AuthorizedHttp.Refused(new Uri("https://evil.example.net/x"));
        Assert.IsType<TransportError>(r);
        Assert.Contains("evil.example.net", r.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_shared_clients_keep_no_cookies_and_follow_no_redirects()
    {
        using var handler = JmapHttp.CreateHandler();
        Assert.False(handler.UseCookies);
        Assert.False(handler.AllowAutoRedirect);
    }

    [Theory]
    [InlineData("http://mail.example.org", false)]
    [InlineData("http://192.168.1.10:8080", false)]
    [InlineData("http://localhost:8080", true)]
    [InlineData("http://127.0.0.1", true)]
    [InlineData("http://127.4.5.6:9000/", true)]
    [InlineData("http://[::1]:8080", true)]
    [InlineData("https://mail.example.org", true)]
    [InlineData("mail.example.org", true)]
    public void Plain_http_is_for_this_computer_only(string typed, bool accepted)
    {
        var r = ServerEndpoint.Read(typed);
        Assert.Equal(accepted, r.IsOk);
        if (!accepted)
        {
            Assert.Equal(ServerEndpoint.InsecureMessage, r.Error!.Message);
        }
    }

    [Fact]
    public void A_host_that_begins_with_http_gets_https()
    {
        Assert.Equal("https://httpbin.example.org/", ServerEndpoint.Parse("httpbin.example.org")!.BaseUri.ToString());
        Assert.Equal("Enter your server address.", ServerEndpoint.Read("  ").Error!.Message);
    }
}
