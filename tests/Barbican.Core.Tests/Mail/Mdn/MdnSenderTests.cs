// M2 plan §6.3 step 4 exit tests (J-068): the receipt is built (MimeKit parses it back: multipart/report with two parts and the disposition
// fields) and sent against FakeJmap: imported into Sent with $seen, submitted with an explicit envelope, $MDNSent set; a refused submission
// destroys the imported copy; nothing at all on a read-only account. Email/import is actions' FakeJmap arm, not merged into this branch yet:
// ImportingClient answers that one method from the test (the integrator drops it once the arm is in).

using System.Text;
using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Transport;
using Barbican.Core.Mail.Mdn;
using Barbican.Core.Tests.Jmap;
using Barbican.FakeJmap;
using MimeKit;

namespace Barbican.Core.Tests.Mail.Mdn;

public sealed class MdnSenderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void The_report_parses_back_with_mimekit()
    {
        var raw = MdnMessage.Build(
            new MdnOptions { To = "anna@example.com", FromEmail = "me@example.org", FromName = "Me", OriginalMessageId = "abc@x", OriginalSubject = "Grüße", OriginalRecipient = "me@example.org" },
            new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.Zero),
            "tok");
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(raw));
        using var message = MimeMessage.Load(stream, Ct);
        Assert.Equal("Read: Grüße", message.Subject);
        Assert.Equal("mdn.tok@example.org", message.MessageId);
        Assert.Equal("abc@x", message.InReplyTo);
        var report = Assert.IsType<MultipartReport>(message.Body);
        Assert.Equal("disposition-notification", report.ReportType);
        Assert.Equal(2, report.Count);
        var human = Assert.IsType<TextPart>(report[0]);
        Assert.Contains("return receipt for the message you sent to me@example.org", human.Text, StringComparison.Ordinal);
        var machine = Assert.IsType<MessageDispositionNotification>(report[1]);
        Assert.Equal("rfc822;me@example.org", machine.Fields["Final-Recipient"]);
        Assert.Equal("<abc@x>", machine.Fields["Original-Message-ID"]);
        Assert.Equal("manual-action/MDN-sent-manually; displayed", machine.Fields["Disposition"]);
    }

    [Fact]
    public async Task A_receipt_is_imported_into_sent_submitted_and_flagged()
    {
        var f = await Fake.StartAsync(new FakeJmapOptions { SeedM2 = true });
        var client = new ImportingClient(f.Client, f.Server);
        var message = await MessageAsync(f, FakeSeed.ReceiptOwnSubject);
        var sender = new MdnSender(TimeProvider.System);

        var r = await sender.SendAsync(client, Upload(f.Client), message, automatic: false, Ct);
        Assert.True(r.IsOk, r.Error?.Message);

        var submission = Assert.Single(f.Server.Api.Submissions);
        Assert.Equal("i1", (string?)submission["identityId"]);
        Assert.Equal("user@example.org", (string?)submission["envelope"]!["mailFrom"]!["email"]);
        Assert.Equal("boss@example.org", (string?)submission["envelope"]!["rcptTo"]![0]!["email"]);
        var imported = f.Server.Store.Email((string)submission["emailId"]!)!;
        Assert.True(imported["mailboxIds"]!.AsObject().ContainsKey(f.Server.Store.MailboxId("sent")!));
        Assert.True((bool)imported["keywords"]!["$seen"]!);
        Assert.StartsWith("Read: ", client.ImportedRaw!.Split("\r\n").First(l => l.StartsWith("Subject:", StringComparison.Ordinal))["Subject: ".Length..], StringComparison.Ordinal);
        Assert.True((bool?)f.Server.Store.Email((string)message["id"]!)!["keywords"]!["$mdnsent"] ?? false);
        Assert.True(sender.SentThisRun((string)message["id"]!));
    }

    [Fact]
    public async Task A_refused_submission_destroys_the_imported_copy()
    {
        var f = await Fake.StartAsync(new FakeJmapOptions { SeedM2 = true });
        var client = new ImportingClient(f.Client, f.Server);
        var message = await MessageAsync(f, FakeSeed.ReceiptOwnSubject);
        var headers = (JsonArray)message["headers"]!;
        headers.Add(new JsonObject { ["name"] = "X-Placeholder", ["value"] = "x" });
        foreach (var h in headers.OfType<JsonObject>().Where(h => string.Equals((string?)h["name"], "Disposition-Notification-To", StringComparison.OrdinalIgnoreCase)))
        {
            h["value"] = " nobody@refused.example";
        }

        var before = f.Server.Store.EmailIds().Count;
        var r = await new MdnSender(TimeProvider.System).SendAsync(client, Upload(f.Client), message, automatic: false, Ct);
        Assert.False(r.IsOk);
        Assert.StartsWith("Not sent: ", r.Error!.Message, StringComparison.Ordinal);
        Assert.NotNull(client.ImportedId);
        Assert.Null(f.Server.Store.Email(client.ImportedId!));
        Assert.Equal(before, f.Server.Store.EmailIds().Count);
        Assert.Empty(f.Server.Api.Submissions);
        Assert.False((bool?)f.Server.Store.Email((string)message["id"]!)!["keywords"]!["$mdnsent"] ?? false);
    }

    [Fact]
    public async Task Nothing_is_sent_from_a_read_only_account()
    {
        var f = await Fake.StartAsync(new FakeJmapOptions { SeedM2 = true }, readOnly: true);
        var message = await MessageAsync(f, FakeSeed.ReceiptOwnSubject);
        var calls = f.Server.ApiCalls.Count;
        var r = await new MdnSender(TimeProvider.System).SendAsync(f.Client, Upload(f.Client), message, automatic: true, Ct);
        Assert.False(r.IsOk);
        Assert.Contains("read-only", r.Error!.Message, StringComparison.Ordinal);
        Assert.Equal(calls, f.Server.ApiCalls.Count);
        Assert.Empty(f.Server.Api.Submissions);
    }

    private static MdnSender.Uploader Upload(JmapClient client) => MdnSender.UploaderFor(client)!;

    private static async Task<JsonNode> MessageAsync(Fake f, string subject)
    {
        var id = f.Server.Store.EmailIds().First(i => string.Equals((string?)f.Server.Store.Email(i)!["subject"], subject, StringComparison.Ordinal));
        var r = await f.Client.SendAsync(
            JmapRequest.Of(JmapRequest.Call("Email/get", new JsonObject { ["accountId"] = f.Client.Session.MailAccountId, ["ids"] = new JsonArray(id), ["properties"] = new JsonArray("id", "subject", "headers", "messageId", "to", "cc", "keywords") }, "g")),
            Ct);
        return r.Value!.Args("g", "Email/get").Value!["list"]![0]!.DeepClone();
    }

    // Answers Email/import from the test (download the uploaded blob, deliver it into the named mailbox); every other call goes to the fake.
    private sealed class ImportingClient(JmapClient inner, FakeJmapServer server) : IJmapClient
    {
        public string? ImportedId { get; private set; }

        public string? ImportedRaw { get; private set; }

        public JmapSession Session => inner.Session;

        public bool IsReadOnly => inner.IsReadOnly;

        public async Task<Result<JmapResponse>> SendAsync(JmapRequest request, CancellationToken ct)
        {
            var call = request.MethodCalls[0];
            if (!string.Equals((string?)call[0], "Email/import", StringComparison.Ordinal))
            {
                return await inner.SendAsync(request, ct);
            }

            var create = call[1]!["emails"]!["m"]!;
            ImportedRaw = Encoding.UTF8.GetString(server.Store.Blob((string)create["blobId"]!)!);
            var mailbox = create["mailboxIds"]!.AsObject().First().Key;
            var subject = ImportedRaw.Split("\r\n").First(l => l.StartsWith("Subject:", StringComparison.Ordinal))["Subject: ".Length..];
            ImportedId = server.Store.Deliver(new FakeMail { Subject = subject, FromEmail = "user@example.org", Text = "receipt", Keywords = ["$seen"] }, mailbox);
            var args = new JsonObject { ["accountId"] = inner.Session.MailAccountId, ["created"] = new JsonObject { ["m"] = new JsonObject { ["id"] = ImportedId } }, ["notCreated"] = null };
            return Result<JmapResponse>.Ok(new JmapResponse { MethodResponses = [new JsonArray("Email/import", args, (string?)call[2])] });
        }

        public Task<Result<Stream>> DownloadAsync(string accountId, string blobId, string name, string type, CancellationToken ct) => inner.DownloadAsync(accountId, blobId, name, type, ct);

        public T? AccountCapability<T>(string accountId, string capability)
            where T : class => inner.AccountCapability<T>(accountId, capability);
    }
}
