// The transport against the fake server: session discovery and redirects (RFC 8620 §2, §2.2), session-state re-fetch (§3.4), core
// limits (§2), request-level problems (§3.6.1), 401 renewal (Rust jmap.rs:242), 429/503 retry of read-only batches (jmap.rs:246), the
// read-only guard (PORT-PLAN.md §4.6). Owner jmap.

using System.Text.Json.Nodes;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Requests;
using Barbican.Core.Jmap.Submission;
using Barbican.Core.Jmap.Transport;
using Barbican.FakeJmap;

namespace Barbican.Core.Tests.Jmap;

public sealed class TransportTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static JmapRequest Echo(int n) =>
        JmapRequest.Of([.. Enumerable.Range(0, n).Select(i => JmapRequest.Call("Core/echo", new JsonObject { ["n"] = i }, "e" + i))]);

    [Fact]
    [Rfc("J-001", "RFC 8620 2.2")]
    public async Task Discovery_follows_a_same_origin_redirect_with_credentials()
    {
        var f = await Fake.StartAsync(new FakeJmapOptions { RedirectWellKnown = true });
        Assert.Equal(["GET /.well-known/jmap", "GET /jmap/session"], f.Server.Requests.Take(2), StringComparer.Ordinal);
        Assert.Equal("b", f.Client.Session.MailAccountId);
        Assert.Equal("user@example.org", f.Client.Session.Username);
    }

    [Fact]
    [Rfc("J-001", "RFC 8620 2.2")]
    public async Task Discovery_never_sends_credentials_to_another_origin()
    {
        var server = new FakeJmapServer(new FakeJmapOptions { RedirectWellKnown = true, RedirectOrigin = "https://other.example.org" });
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => FakeJmapHarness.ConnectAsync(server, TimeProvider.System, ct: Ct));
        Assert.Contains("GET /jmap/session 401", server.Requests, StringComparer.Ordinal);
        Assert.Contains("Wrong username", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Rfc("J-001", "RFC 8620 2.2")]
    public async Task Discovery_refuses_a_downgrade_to_http()
    {
        var server = new FakeJmapServer(new FakeJmapOptions { RedirectWellKnown = true, RedirectOrigin = "http://jmap.example.org" });
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => FakeJmapHarness.ConnectAsync(server, TimeProvider.System, ct: Ct));
        Assert.Contains("insecure", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Rfc("J-002", "RFC 8620 2")]
    public async Task Relative_session_urls_are_resolved_and_templates_keep_their_braces()
    {
        var f = await Fake.StartAsync(new FakeJmapOptions { RelativeUrls = true });
        Assert.Equal("https://jmap.example.org/jmap/", f.Client.Session.ApiUrl);
        Assert.Equal("https://jmap.example.org/jmap/download/{accountId}/{blobId}/{name}?accept={type}", f.Client.Session.DownloadUrl);
        Assert.StartsWith("https://jmap.example.org/jmap/eventsource/?types={types}", f.Client.Session.EventSourceUrl, StringComparison.Ordinal);
        Assert.True((await f.Client.SendAsync(Echo(1), Ct)).IsOk);
    }

    [Theory]
    [Rfc("J-002", "RFC 8620 2")]
    [InlineData("https://h.example.org/a/b", "/x", "https://h.example.org/x")]
    [InlineData("https://h.example.org/a/b", "x/{y}", "https://h.example.org/a/x/{y}")]
    [InlineData("https://h.example.org/a/b", "//c.example.org/z", "https://c.example.org/z")]
    [InlineData("https://h.example.org/a/b", "https://d.example.org/", "https://d.example.org/")]
    public void Absolute_resolves_like_a_browser(string session, string url, string expected) =>
        Assert.Equal(expected, SessionParser.Absolute(new Uri(session), url));

    [Fact]
    [Rfc("J-003", "RFC 8620 2")]
    public async Task Primary_accounts_and_account_level_capabilities()
    {
        var f = await Fake.StartAsync(new FakeJmapOptions { MaxDelayedSend = 7200 });
        Assert.Equal("b", f.Client.Session.AccountFor(JmapCapabilities.Submission));
        var cap = f.Client.AccountCapability<SubmissionCapability>("b", JmapCapabilities.Submission);
        Assert.Equal(7200, cap!.MaxDelayedSend);
        Assert.NotNull(f.Client.AccountCapability<JsonObject>("b", JmapCapabilities.Core));
        Assert.Null(f.Client.AccountCapability<JsonObject>("b", "urn:example:none"));
    }

    [Fact]
    [Rfc("J-005", "RFC 8620 3.4")]
    public async Task A_changed_session_state_refetches_the_session()
    {
        var f = await Fake.StartAsync();
        Assert.Equal("S1", f.Client.Session.State);
        f.Server.BumpSessionState();
        Assert.True((await f.Client.SendAsync(Echo(1), Ct)).IsOk);
        Assert.Equal("S2", f.Client.Session.State);
        Assert.Equal(2, f.Server.Requests.Count(r => r.EndsWith("/.well-known/jmap", StringComparison.Ordinal)));
    }

    [Fact]
    [Rfc("J-006", "RFC 8620 3.3")]
    public async Task Using_names_capabilities_and_an_unknown_one_is_a_problem()
    {
        var f = await Fake.StartAsync();
        Assert.True((await f.Client.SendAsync(Echo(1), Ct)).IsOk);
        var bad = await f.Client.SendAsync(Echo(1) with { Using = ["urn:example:nope"] }, Ct);
        var p = Assert.IsType<ProblemError>(bad.Error);
        Assert.Equal("urn:ietf:params:jmap:error:unknownCapability", p.Type);
    }

    [Fact]
    [Rfc("J-007", "RFC 8620 2")]
    public async Task Independent_calls_are_split_by_maxCallsInRequest()
    {
        var f = await Fake.StartAsync(new FakeJmapOptions { MaxCallsInRequest = 3 });
        var r = await f.Client.SendAsync(Echo(7), Ct);
        Assert.Equal(7, r.Value!.MethodResponses.Count);
        Assert.Equal(3, f.Server.ApiCalls.Count);
    }

    [Fact]
    [Rfc("J-008", "RFC 8620 5.1")]
    [Rfc("J-009", "RFC 8620 5.3")]
    public async Task GetAll_chunks_by_maxObjectsInGet_and_limits_are_read()
    {
        var f = await Fake.StartAsync(new FakeJmapOptions { MaxObjectsInGet = 2, MaxObjectsInSet = 7 });
        var ids = f.Server.Store.EmailIds();
        var all = await f.Client.GetAllAsync("Email/get", "b", ids, new JsonArray("id"), Ct);
        Assert.Equal(ids.Count, all.Value!.Count);
        Assert.Equal(1, f.Server.ApiCalls.Count(c => c.StartsWith("Email/get", StringComparison.Ordinal)));
        Assert.Equal((ids.Count + 1) / 2, f.Server.ApiCalls.Single().Split(',').Length);
        Assert.Equal(7, f.Client.Session.Limits.MaxObjectsInSet);
    }

    [Fact]
    [Rfc("J-010", "RFC 8620 2")]
    public async Task A_request_over_maxSizeRequest_is_refused_before_sending()
    {
        var f = await Fake.StartAsync(new FakeJmapOptions { MaxSizeRequest = 300 });
        var big = JmapRequest.Of(JmapRequest.Call("Core/echo", new JsonObject { ["x"] = new string('a', 400) }, "e"));
        var r = await f.Client.SendAsync(big, Ct);
        Assert.IsType<TransportError>(r.Error);
        Assert.Empty(f.Server.ApiCalls);
    }

    [Fact]
    [Rfc("J-011", "RFC 8620 2")]
    public async Task Concurrent_requests_stay_within_maxConcurrentRequests()
    {
        var f = await Fake.StartAsync(new FakeJmapOptions { MaxConcurrentRequests = 2 });
        Task<Barbican.Core.Common.Result<JmapResponse>>[] sends;
        using (f.Server.HoldApi())
        {
            sends = [.. Enumerable.Range(0, 6).Select(_ => f.Client.SendAsync(Echo(1), Ct))];
            await Poll.UntilAsync(() => f.Client.RequestsInFlight == 2 && f.Server.ApiCalls.Count == 2, "two requests in flight");
        }

        var results = await Task.WhenAll(sends);
        Assert.All(results, r => Assert.True(r.IsOk));
        Assert.Equal(2, f.Server.MaxInFlight);
        Assert.Equal(6, f.Server.ApiCalls.Count);
    }

    [Fact]
    [Rfc("J-012", "RFC 8620 6.1")]
    public async Task Uploads_check_maxSizeUpload_first()
    {
        var f = await Fake.StartAsync(new FakeJmapOptions { MaxSizeUpload = 10 });
        var tooBig = await f.Client.UploadAsync("b", new byte[11], "application/pdf", Ct);
        Assert.Contains("larger", tooBig.Error!.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("POST upload", f.Server.Requests, StringComparer.Ordinal);
        var ok = await f.Client.UploadAsync("b", "hello"u8.ToArray(), "text/plain", Ct);
        Assert.Equal(5, ok.Value!.Size);
        Assert.Equal("hello"u8.ToArray(), f.Server.Store.Blob(ok.Value.BlobId));
    }

    [Fact]
    [Rfc("J-013", "RFC 8620 5.5")]
    public async Task QueryAll_pages_when_the_server_caps_limit()
    {
        var f = await Fake.StartAsync(new FakeJmapOptions { QueryLimitCap = 3 });
        var ids = await f.Client.QueryAllAsync("Email/query", "b", [], Ct);
        Assert.Equal(f.Server.Store.EmailIds(), ids.Value);
        Assert.Equal(4, f.Server.ApiCalls.Count);
    }

    [Fact]
    [Rfc("J-016", "RFC 8620 3.6.1")]
    public async Task Request_level_problems_become_problem_errors()
    {
        var f = await Fake.StartAsync();
        f.Server.Inject(FakeFault.Problem);
        var r = await f.Client.SendAsync(Echo(1), Ct);
        var p = Assert.IsType<ProblemError>(r.Error);
        Assert.Equal(500, p.Status);
        Assert.False(string.IsNullOrEmpty(p.Message));
    }

    [Fact]
    [Rfc("J-017", "RFC 8620 3.6.2")]
    public async Task Method_errors_name_the_method_called()
    {
        var f = await Fake.StartAsync();
        var r = await f.Client.CallAsync(JmapRequest.Of(JmapRequest.Call("Email/query", new JsonObject { ["accountId"] = "b", ["anchor"] = "nope" }, "q")), Ct);
        var e = Assert.IsType<MethodError>(r.Error);
        Assert.Equal(("Email/query", "anchorNotFound"), (e.Method, e.Type));
        var u = await f.Client.CallAsync(JmapRequest.Of(JmapRequest.Call("Foo/bar", new JsonObject { ["accountId"] = "b" }, "x")), Ct);
        Assert.Equal("unknownMethod", Assert.IsType<MethodError>(u.Error).Type);
    }

    [Fact]
    [Rfc("J-020", "RFC 6750 3.1")]
    public async Task A_401_renews_once_and_retries_for_api_download_and_push()
    {
        var server = new FakeJmapServer();
        var tokens = new RenewingTokens(() => Task.FromResult<string?>(server.IssueToken()));
        var http = new JmapHttp(() => server);
        var session = await SessionFetcher.FetchAsync(http, tokens, new Uri(server.Base + "/.well-known/jmap"), TimeSpan.FromSeconds(30), TimeProvider.System, Ct);
        using var client = new JmapClient(http, tokens, session.Value!, new JmapClientOptions(), TimeProvider.System, JmapTrace.Off);

        server.Inject(FakeFault.Unauthorized);
        Assert.True((await client.SendAsync(Echo(1), Ct)).IsOk);
        var blob = server.Store.Email(server.Store.EmailIds()[0])!["blobId"]!.GetValue<string>();
        server.Inject(FakeFault.Unauthorized);
        await using (var s = (await client.DownloadAsync("b", blob, "m.eml", "message/rfc822", Ct)).Value!)
        {
            Assert.True(s.CanRead);
        }

        server.Inject(FakeFault.Unauthorized);
        await using (var push = (await client.OpenEventSourceAsync(["Email"], 30, Ct)).Value!)
        {
            Assert.Equal(1, server.Push.Opened);
        }

        Assert.Equal(3, tokens.Renewals);
    }

    [Fact]
    [Rfc("J-020", "RFC 6750 3.1")]
    public async Task A_second_401_or_a_failed_renewal_asks_for_sign_in()
    {
        var f = await Fake.StartAsync();
        f.Server.Inject(FakeFault.Unauthorized);
        var r = await f.Client.SendAsync(Echo(1), Ct);
        Assert.True(Assert.IsType<AuthError>(r.Error).NeedsSignIn);
        Assert.Empty(f.Server.ApiCalls);
    }

    [Fact]
    [Rfc("J-018", "RFC 8620 3.6.1")]
    public async Task A_read_only_batch_is_retried_once_after_retry_after_capped_at_10_s()
    {
        var clock = Fake.VirtualClock();
        var f = await Fake.StartAsync(time: clock);
        f.Server.RetryAfterSeconds = 60;
        f.Server.Inject(FakeFault.TooManyRequests);
        var send = f.Client.SendAsync(Echo(1), Ct);
        await Poll.UntilAsync(() => f.Server.ApiCalls.Count == 1, "the first attempt");
        clock.Advance(TimeSpan.FromSeconds(9.9));
        await Task.Delay(50, Ct);
        Assert.Single(f.Server.ApiCalls);
        clock.Advance(TimeSpan.FromSeconds(0.1));
        Assert.True((await send).IsOk);
        Assert.Equal(2, f.Server.ApiCalls.Count);
    }

    [Fact]
    [Rfc("J-018", "RFC 8620 3.6.1")]
    public async Task A_write_is_never_retried()
    {
        var f = await Fake.StartAsync();
        f.Server.Inject(FakeFault.Unavailable);
        var set = JmapRequest.Of(JmapRequest.Call("Email/set", new JsonObject { ["accountId"] = "b", ["update"] = new JsonObject() }, "s"));
        var r = await f.Client.SendAsync(set, Ct);
        Assert.IsType<ProblemError>(r.Error);
        Assert.Single(f.Server.ApiCalls);

        f.Server.Inject(FakeFault.TooManyRequests);
        var limited = await f.Client.SendAsync(set, Ct);
        Assert.Contains("rate limiting", limited.Error!.Message, StringComparison.Ordinal);
        Assert.Equal(2, f.Server.ApiCalls.Count);
    }

    [Theory]
    [InlineData("Email/set")]
    [InlineData("Mailbox/set")]
    [InlineData("EmailSubmission/set")]
    [InlineData("Email/import")]
    [InlineData("Email/copy")]
    [InlineData("Identity/set")]
    [InlineData("VacationResponse/set")]
    public async Task The_read_only_client_refuses_every_write(string method)
    {
        var f = await Fake.StartAsync(readOnly: true);
        var r = await f.Client.SendAsync(JmapRequest.Of(JmapRequest.Call("Mailbox/get", new JsonObject { ["accountId"] = "b" }, "a"), JmapRequest.Call(method, new JsonObject { ["accountId"] = "b" }, "w")), Ct);
        Assert.Equal(method, Assert.IsType<ReadOnlyViolation>(r.Error).Method);
        Assert.Empty(f.Server.ApiCalls);
        Assert.IsType<ReadOnlyViolation>((await f.Client.UploadAsync("b", new byte[1], "x/y", Ct)).Error);
        Assert.True((await f.Client.SendAsync(Echo(1), Ct)).IsOk);
    }

    [Fact]
    [Rfc("J-035", "RFC 8620 6.2")]
    public async Task Download_expands_the_template_percent_encoded()
    {
        Assert.Equal(
            "https://h/d/b/B%2F1/a%20b%C3%BC.pdf?accept=application%2Fpdf",
            UriTemplates.Download("https://h/d/{accountId}/{blobId}/{name}?accept={type}", "b", "B/1", "a bü.pdf", "application/pdf"));
        Assert.EndsWith("/file?accept=application%2Foctet-stream", UriTemplates.Download("https://h/{name}?accept={type}", "b", "x", "", ""), StringComparison.Ordinal);
        var f = await Fake.StartAsync();
        var email = f.Server.Store.Emails(f.Server.Store.EmailIds()).First(e => string.Equals((string?)e["subject"], "Report with attachments", StringComparison.Ordinal));
        var part = email["attachments"]![0]!;
        await using var s = (await f.Client.DownloadAsync("b", (string)part["blobId"]!, (string)part["name"]!, (string)part["type"]!, Ct)).Value!;
        using var reader = new StreamReader(s);
        Assert.StartsWith("%PDF", await reader.ReadToEndAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    [Rfc("J-037", "RFC 8620 7.3")]
    public async Task The_event_source_url_names_types_closeafter_and_ping()
    {
        Assert.Equal("https://h/es?types=Email,Mailbox&closeafter=no&ping=30", UriTemplates.EventSource("https://h/es?types={types}&closeafter={closeafter}&ping={ping}", ["Email", "Mailbox"], 30));
        Assert.Equal("https://h/es?types=*", UriTemplates.EventSource("https://h/es?types={types}", [], 30));
        var f = await Fake.StartAsync();
        await using var s = (await f.Client.OpenEventSourceAsync(["Email", "Mailbox", "Thread", "EmailDelivery", "EmailSubmission"], 30, Ct)).Value!;
        Assert.Contains("GET eventsource types=Email,Mailbox,Thread,EmailDelivery,EmailSubmission closeafter=no ping=30", f.Server.Requests, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Session_failures_are_named()
    {
        Assert.False(SessionParser.Parse("not json", new Uri("https://h/")).IsOk);
        Assert.Contains("No mail account", SessionParser.Parse("{\"accounts\":{}}", new Uri("https://h/")).Error!.Message, StringComparison.Ordinal);
        Assert.Contains("mail capability", SessionParser.Parse("{\"accounts\":{},\"apiUrl\":\"/a\",\"downloadUrl\":\"/d\",\"primaryAccounts\":{}}", new Uri("https://h/")).Error!.Message, StringComparison.Ordinal);
        var server = new FakeJmapServer(new FakeJmapOptions { Password = "other" });
        var http = new JmapHttp(() => server);
        var r = await SessionFetcher.FetchAsync(http, StaticAccessTokenSource.Basic("user@example.org", "wrong"), new Uri(server.Base + "/.well-known/jmap"), TimeSpan.FromSeconds(5), TimeProvider.System, Ct);
        Assert.True(Assert.IsType<AuthError>(r.Error).NeedsSignIn);
    }
}
