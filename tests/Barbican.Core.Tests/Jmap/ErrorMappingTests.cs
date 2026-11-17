// Error-to-message mapping and /set result handling: RFC 8620 §3.6.2 (method errors), §5.3 (SetError types, created / updated /
// destroyed / notCreated / notUpdated / notDestroyed), RFC 8621 §2.3, §4.6, §7.5 (mail SetError types); Rust jmap.rs method_error,
// set_error; RFC 9110 §10.2.3 (Retry-After). Owner jmap.

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Transport;
using Barbican.FakeJmap;

namespace Barbican.Core.Tests.Jmap;

public sealed class ErrorMappingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [Rfc("J-017", "RFC 8620 3.6.2")]
    [InlineData("stateMismatch", null, "Someone else changed this at the same time; try again.")]
    [InlineData("tooManyChanges", null, "Too many changes at once; reload.")]
    [InlineData("cannotCalculateChanges", null, "The list changed too much; reload.")]
    [InlineData("anchorNotFound", null, "The list changed; reload.")]
    [InlineData("serverUnavailable", "busy", "The server had a problem (serverUnavailable): busy; try again.")]
    [InlineData("unsupportedSort", null, "The server can't sort or search like that.")]
    [InlineData("unsupportedFilter", "text", "The server can't sort or search like that: text.")]
    [InlineData("unknownMethod", null, "The server does not support this (unknownMethod).")]
    [InlineData("accountReadOnly", null, "You do not have permission to do that.")]
    [InlineData("accountNotFound", null, "This account does not support that.")]
    [InlineData("requestTooLarge", null, "Too many items in one request.")]
    [InlineData("weird", null, "Server error: weird")]
    [InlineData("weird", "why", "weird: why")]
    public void Method_errors_read_as_sentences(string type, string? description, string expected) =>
        Assert.Equal(expected, MethodErrors.Message(type, description));

    [Theory]
    [Rfc("J-019", "RFC 8620 5.3")]
    [InlineData("notFound", null, null, "It no longer exists.")]
    [InlineData("forbidden", null, null, "You do not have permission to do that.")]
    [InlineData("singleton", null, null, "This object can't be created or deleted.")]
    [InlineData("rateLimit", null, null, "Too many messages sent recently; try again later.")]
    [InlineData("forbiddenMailFrom", null, null, "The server refused the sender address.")]
    [InlineData("forbiddenToSend", null, null, "This account may not send mail.")]
    [InlineData("forbiddenFrom", null, null, "You may not send from that address.")]
    [InlineData("cannotUnsend", null, null, "The message has already been sent.")]
    [InlineData("tooManyKeywords", null, null, "The message has too many tags.")]
    [InlineData("tooManyMailboxes", null, null, "The message is in too many folders.")]
    [InlineData("overQuota", null, null, "Your mailbox is full.")]
    [InlineData("tooLarge", null, null, "The message is too large.")]
    [InlineData("invalidEmail", "no date", null, "The message is invalid: no date.")]
    [InlineData("tooManyRecipients", null, null, "Too many recipients.")]
    [InlineData("noRecipients", null, null, "Add at least one recipient.")]
    [InlineData("invalidRecipients", null, null, "Some recipients are invalid.")]
    [InlineData("mailboxHasChild", null, null, "Delete or move its subfolders first.")]
    [InlineData("mailboxHasEmail", null, null, "The folder is not empty.")]
    [InlineData("blobNotFound", null, null, "An attachment is no longer on the server; attach it again.")]
    [InlineData("alreadyExists", null, null, "That already exists.")]
    [InlineData("willDestroy", null, null, "It is being deleted.")]
    [InlineData("invalidPatch", "bad path", null, "The change is invalid: bad path.")]
    [InlineData("invalidProperties", null, "name,role", "Invalid name, role")]
    [InlineData("other", null, null, "other")]
    [InlineData("other", "x", null, "other: x")]
    public void Set_errors_read_as_sentences(string type, string? description, string? properties, string expected) =>
        Assert.Equal(expected, SetErrors.Message(type, description, properties?.Split(',')));

    [Fact]
    [Rfc("J-032", "RFC 8620 5.3")]
    public void A_set_response_reads_into_created_updated_destroyed_and_failures()
    {
        var o = SetErrors.Read(JsonNode.Parse("""
            {"created":{"c1":{"id":"M9"},"c2":{}},"updated":{"e1":null,"e2":{"size":3}},"destroyed":["e3"],
             "notCreated":{"c3":{"type":"overQuota"}},"notUpdated":{"e4":{"type":"invalidProperties","properties":["keywords"],"description":"bad"}},
             "notDestroyed":{"e5":{}}}
            """));
        Assert.Equal("M9", o.Created["c1"]);
        Assert.False(o.Created.ContainsKey("c2"));
        Assert.Equal(["e1", "e2"], o.Updated);
        Assert.Equal(["e3"], o.Destroyed);
        Assert.Equal(["c3: Your mailbox is full.", "e4: Invalid keywords: bad", "e5: error"], o.Failed.Select(f => f.Message), StringComparer.Ordinal);
        Assert.Equal(["keywords"], o.Failed[1].Properties!);
        var empty = SetErrors.Read(null);
        Assert.Empty(empty.Created);
        Assert.Empty(empty.Failed);
    }

    [Fact]
    [Rfc("J-032", "RFC 8620 5.3")]
    public async Task Email_set_against_the_server_reports_what_failed()
    {
        var f = await Fake.StartAsync();
        var id = f.Server.Store.EmailIds()[0];
        var r = await f.Client.SendAsync(JmapRequest.Of(JmapRequest.Call("Email/set", new JsonObject
        {
            ["accountId"] = "b",
            ["update"] = new JsonObject { [id] = new JsonObject { ["keywords/$flagged"] = true }, ["gone"] = new JsonObject { ["keywords/$seen"] = true } },
            ["destroy"] = new JsonArray("missing"),
        }, "s")), Ct);
        var outcome = SetErrors.Read(r.Value!.MethodResponses[0][1]);
        Assert.Equal([id], outcome.Updated);
        Assert.Equal(["gone: It no longer exists.", "missing: It no longer exists."], outcome.Failed.Select(e => e.Message), StringComparer.Ordinal);
        Assert.True(f.Server.Store.Email(id)!["keywords"]!["$flagged"]!.GetValue<bool>());
    }

    [Fact]
    [Rfc("J-018", "RFC 9110 10.2.3")]
    public void Retry_after_reads_seconds_or_a_date_defaults_to_2_s_and_is_capped()
    {
        var now = new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);
        var cap = TimeSpan.FromSeconds(10);
        Assert.Equal(TimeSpan.FromSeconds(2), RetryAfter.Delay(null, now, cap));
        Assert.Equal(TimeSpan.FromSeconds(4), RetryAfter.Delay(new RetryConditionHeaderValue(TimeSpan.FromSeconds(4)), now, cap));
        Assert.Equal(cap, RetryAfter.Delay(new RetryConditionHeaderValue(TimeSpan.FromMinutes(5)), now, cap));
        Assert.Equal(TimeSpan.FromSeconds(7), RetryAfter.Delay(new RetryConditionHeaderValue(now.AddSeconds(7)), now, cap));
        Assert.Equal(TimeSpan.Zero, RetryAfter.Delay(new RetryConditionHeaderValue(now.AddSeconds(-7)), now, cap));
        Assert.True(RetryAfter.IsRetryStatus(HttpStatusCode.ServiceUnavailable));
        Assert.False(RetryAfter.IsRetryStatus(HttpStatusCode.InternalServerError));
    }

    [Fact]
    [Rfc("J-001", "RFC 8620 2.2")]
    public async Task Session_lookup_failures()
    {
        var server = new FakeJmapServer(new FakeJmapOptions { RedirectWellKnown = true });
        var http = new JmapHttp(() => server);
        var auth = StaticAccessTokenSource.Basic("user@example.org", "fake-password");
        var missing = await Sessions.FetchAsync(http, auth, new Uri(server.Base + "/nothing"), TimeSpan.FromSeconds(5), TimeProvider.System, Ct);
        Assert.Contains("404", missing.Error!.Message, StringComparison.Ordinal);
        var offline = await Sessions.FetchAsync(http, auth, new Uri("https://offline.example.org/.well-known/jmap"), TimeSpan.FromSeconds(5), TimeProvider.System, Ct);
        Assert.True(Assert.IsType<TransportError>(offline.Error).Retryable);
        Assert.False(Sessions.Parse("[]", new Uri("https://h/")).IsOk);
    }

    [Fact]
    public async Task The_factory_connects_and_writes_the_trace_file()
    {
        var server = new FakeJmapServer();
        var path = Path.Combine(Path.GetTempPath(), $"barbican-trace-{Guid.NewGuid():N}.txt");
        try
        {
            var factory = new JmapClientFactory(new JmapHttp(() => server), TimeProvider.System);
            var client = (await factory.ConnectAsync(ServerEndpoint.Parse(server.Base)!, StaticAccessTokenSource.Bearer(FakeJmapServer.AccessToken), new JmapClientOptions { TracePath = path }, Ct)).Value!;
            Assert.True(client.Trace.IsOn);
            await client.SendAsync(JmapRequest.Of(JmapRequest.Call("Core/echo", [], "e")), Ct);
            Assert.Equal(["SESSION", "Core/echo -> Core/echo"], await File.ReadAllLinesAsync(path, Ct));
            var bad = await factory.ConnectAsync(ServerEndpoint.Parse(server.Base)!, StaticAccessTokenSource.Bearer("nope"), new JmapClientOptions(), Ct);
            Assert.IsType<AuthError>(bad.Error);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Trace_lines_name_methods_limits_and_errors_only()
    {
        var lines = new List<string>();
        var t = JmapTrace.To(lines.Add);
        JsonArray[] calls = [JmapRequest.Call("Email/query", new JsonObject { ["limit"] = 50, ["filter"] = new JsonObject { ["text"] = "secret" } }, "0")];
        t.Request(calls, [["error", new JsonObject { ["type"] = "anchorNotFound" }, "0"]]);
        t.Request(calls, null);
        t.Event("PUSH", "");
        Assert.Equal(["Email/query(limit=50) -> error errors=\"anchorNotFound\"", "Email/query(limit=50) -> (failed)", "PUSH"], lines);
        Assert.False(JmapTrace.Off.IsOn);
        Assert.Same(JmapTrace.Off, JmapTrace.ToFile(""));
    }

    [Fact]
    public void One_client_per_origin()
    {
        using var http = new JmapHttp();
        var a = http.ClientFor(new Uri("https://a.example.org/x"));
        Assert.Same(a, http.ClientFor(new Uri("https://a.example.org/y")));
        Assert.NotSame(a, http.ClientFor(new Uri("https://b.example.org/")));
        Assert.Equal(Timeout.InfiniteTimeSpan, a.Timeout);
    }
}
