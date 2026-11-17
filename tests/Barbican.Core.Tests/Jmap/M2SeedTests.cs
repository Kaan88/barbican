// New tests (stage F of M2): the M2 seed is opt-in and the M1 data and session are unchanged; the new dispatch arms answer unknownMethod until their owner fills them;
// the synthetic TNEF and the heavy HTML body have the documented shape. Owner: stage F, frozen with the seed.

using System.Buffers.Binary;
using System.Text.Json.Nodes;
using Barbican.FakeJmap;

namespace Barbican.Core.Tests.Jmap;

public sealed class M2SeedTests
{
    private static JsonObject Request(string capability, string method, JsonObject args) => new()
    {
        ["using"] = new JsonArray("urn:ietf:params:jmap:core", capability),
        ["methodCalls"] = new JsonArray(new JsonArray(method, args, "c0")),
    };

    private static JsonObject Email(FakeJmapServer server, string subject) =>
        server.Store.Emails(server.Store.EmailIds()).Single(e => string.Equals((string?)e["subject"], subject, StringComparison.Ordinal));

    [Fact]
    public void The_m1_seed_is_unchanged_unless_the_m2_seed_is_asked_for()
    {
        var m1 = new FakeJmapServer();
        var m2 = new FakeJmapServer(new FakeJmapOptions { SeedM2 = true });
        Assert.DoesNotContain(m1.Store.Emails(m1.Store.EmailIds()), e => string.Equals((string?)e["subject"], FakeSeed.NewsletterSubject, StringComparison.Ordinal));
        Assert.True(m2.Store.EmailIds().Count > m1.Store.EmailIds().Count + 15);
    }

    [Fact]
    public void The_m2_seed_carries_the_headers_and_attachments_the_streams_need()
    {
        var server = new FakeJmapServer(new FakeJmapOptions { SeedM2 = true });
        var news = Email(server, FakeSeed.NewsletterSubject);
        var headers = ((JsonArray)news["headers"]!).Select(h => (string)h!["name"]!).ToList();
        Assert.Contains("List-Unsubscribe", headers, StringComparer.Ordinal);
        Assert.Contains("List-Unsubscribe-Post", headers, StringComparer.Ordinal);
        Assert.Contains("dkim=pass header.d=news.example", (string)((JsonArray)news["header:Authentication-Results:all"]!)[0]!, StringComparison.Ordinal);

        Assert.Contains(((JsonArray)Email(server, FakeSeed.ReceiptOwnSubject)["headers"]!).Select(h => (string)h!["name"]!), n => string.Equals(n, "Disposition-Notification-To", StringComparison.Ordinal));
        Assert.Contains(((JsonArray)Email(server, FakeSeed.SpamHeadersSubject)["headers"]!).Select(h => (string)h!["value"]!), v => v.Contains("score=7.5", StringComparison.Ordinal));
        Assert.Equal("text/calendar", (string)Email(server, FakeSeed.InvitationSubject)["attachments"]![0]!["type"]!);
        Assert.Equal("message/rfc822", (string)Email(server, FakeSeed.ForwardedSubject)["attachments"]![0]!["type"]!);

        var three = ((JsonArray)Email(server, FakeSeed.ThreeAttachmentsSubject)["attachments"]!).Select(a => (string)a!["name"]!).ToList();
        Assert.Equal(["scan.pdf", "scan.pdf", "notes.txt"], three);

        var tnefBlob = (string)Email(server, FakeSeed.TnefSubject)["attachments"]![0]!["blobId"]!;
        var tnef = server.Store.Blob(tnefBlob)!;
        Assert.Equal(0x223E9F78u, BinaryPrimitives.ReadUInt32LittleEndian(tnef));

        var heavy = (string)Email(server, FakeSeed.HeavyHtmlSubject)["bodyValues"]!["2"]!["value"]!;
        Assert.InRange(heavy.Length, FakeSeed.HeavyHtmlBytes - 1000, FakeSeed.HeavyHtmlBytes + 1000);
    }

    [Fact]
    public void The_m2_dispatch_arms_answer_unknown_method_until_their_stream_fills_them()
    {
        var server = new FakeJmapServer();
        foreach (var (capability, method) in new[]
        {
            ("urn:ietf:params:jmap:mail", "Mailbox/set"),
            ("urn:ietf:params:jmap:mail", "Email/import"),
            ("urn:ietf:params:jmap:mail", "Email/copy"),
            ("urn:ietf:params:jmap:mail", "Email/parse"),
            ("urn:ietf:params:jmap:core", "Blob/copy"),
            ("urn:ietf:params:jmap:mail", "SearchSnippet/get"),
            ("urn:ietf:params:jmap:submission", "EmailSubmission/set"),
            ("urn:ietf:params:jmap:sieve", "SieveScript/get"),
            ("urn:ietf:params:jmap:sieve", "SieveScript/set"),
            ("urn:ietf:params:jmap:sieve", "SieveScript/validate"),
            ("urn:ietf:params:jmap:filenode", "FileNode/get"),
            ("urn:ietf:params:jmap:filenode", "FileNode/query"),
            ("urn:ietf:params:jmap:filenode", "FileNode/set"),
            ("urn:ietf:params:jmap:calendars", "CalendarEvent/parse"),
            ("urn:stalwart:jmap", "x:ArchivedItem/get"),
            ("urn:stalwart:jmap", "x:ArchivedItem/query"),
            ("urn:stalwart:jmap", "x:ArchivedItem/set"),
        })
        {
            var (status, body) = server.Api.Run(Request(capability, method, new JsonObject { ["accountId"] = server.Options.AccountId }));
            Assert.Equal(200, status);
            var response = (JsonArray)((JsonArray)body["methodResponses"]!)[0]!;
            Assert.Equal("error", (string)response[0]!);
            Assert.Equal("unknownMethod", (string)response[1]!["type"]!);
        }
    }
}
