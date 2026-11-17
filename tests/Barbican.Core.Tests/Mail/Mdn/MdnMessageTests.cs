// Ported from Rust src/mdn.rs tests (:246-:334). Red until stream reader implements MdnMessage (M2 plan 6.3, T-177..T-183).

using System.Text.Json.Nodes;
using Barbican.Core.Mail.Mdn;

namespace Barbican.Core.Tests.Mail.Mdn;

public sealed class MdnMessageTests
{
    private static JsonNode J(string json) => JsonNode.Parse(json)!;

    private static JsonArray Arr(string json) => (JsonArray)J(json);

    private static DateTimeOffset Now() => DateTimeOffset.Parse("2026-05-28T14:23:00Z", System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    [Red("reader")]
    [RustTest("mdn::dnt_addresses")]
    public void Dnt_addresses()
    {
        Assert.Equal("a@x.example", MdnMessage.FirstAddress("a@x.example, b@y.example"));
        Assert.Equal("jane@x.example", MdnMessage.FirstAddress("\"Doe, Jane\" <jane@x.example>, b@y"));
        Assert.Equal("a@x", MdnMessage.FirstAddress("<a@x>"));
        Assert.Null(MdnMessage.FirstAddress("Jane Doe"));
        Assert.Null(MdnMessage.FirstAddress("a@b@c"));
        Assert.Null(MdnMessage.FirstAddress(string.Empty));
    }

    [Fact]
    [Red("reader")]
    [RustTest("mdn::auto_send_only_to_the_return_path")]
    public void Auto_send_only_to_the_return_path()
    {
        static JsonNode M(string dnt, string? returnPath)
        {
            var headers = new JsonArray { JsonNode.Parse($$"""{"name": "Disposition-Notification-To", "value": "{{dnt}}"}""") };
            if (returnPath is not null)
            {
                headers.Add(JsonNode.Parse($$"""{"name": "Return-Path", "value": "{{returnPath}}"}"""));
            }

            return new JsonObject { ["headers"] = headers, ["from"] = JsonNode.Parse("""[{"email": "boss@corp.example"}]""") };
        }

        Assert.True(MdnMessage.AutoSendOk(M("Boss <boss@corp.example>", "<BOSS@corp.example>")));
        Assert.False(MdnMessage.AutoSendOk(M("tracker@spy.example", "<boss@corp.example>")), "a third party is asked for");
        Assert.True(MdnMessage.AutoSendOk(M("boss@corp.example", null)), "no Return-Path: From counts");
        Assert.False(MdnMessage.AutoSendOk(M("tracker@spy.example", null)));
    }

    [Fact]
    [Red("reader")]
    [RustTest("mdn::builds_two_part_report")]
    public void Builds_two_part_report()
    {
        var o = new MdnOptions
        {
            To = "anna@example.com",
            FromEmail = "me@example.org",
            FromName = "Me Myself",
            OriginalMessageId = "abc@x",
            OriginalSubject = "Hello",
            OriginalRecipient = "me@example.org",
            Automatic = false,
        };
        var raw = MdnMessage.Build(o, Now(), "tok");
        Assert.StartsWith("Date: Thu, 28 May 2026 14:23:00 +0000\r\nFrom: Me Myself <me@example.org>\r\nTo: anna@example.com\r\nSubject: Read: Hello\r\n", raw, StringComparison.Ordinal);
        Assert.Contains("Message-ID: <mdn.tok@example.org>\r\nIn-Reply-To: <abc@x>\r\n", raw, StringComparison.Ordinal);
        Assert.Contains("Content-Type: multipart/report; report-type=disposition-notification;\r\n\tboundary=\"----=_MDN_tok\"", raw, StringComparison.Ordinal);
        Assert.Contains("Reporting-UA: example.org; Barbican\r\nFinal-Recipient: rfc822;me@example.org\r\nOriginal-Recipient: rfc822;me@example.org\r\nOriginal-Message-ID: <abc@x>\r\nDisposition: manual-action/MDN-sent-manually; displayed", raw, StringComparison.Ordinal);
        Assert.EndsWith("\r\n------=_MDN_tok--\r\n", raw, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', raw.Replace("\r\n", string.Empty, StringComparison.Ordinal));

        var auto = MdnMessage.Build(o with { Automatic = true }, Now(), "t");
        Assert.Contains("automatic-action/MDN-sent-automatically; displayed", auto, StringComparison.Ordinal);
    }

    [Fact]
    [Red("reader")]
    [RustTest("mdn::header_injection_is_neutralised")]
    public void Header_injection_is_neutralised()
    {
        // GHSA-w38p-hpqv-g89c: a CRLF smuggled in via the subject, Message-ID or requester
        var o = new MdnOptions
        {
            To = "a@b.example\r\nBcc: victim@x",
            FromEmail = "me@ex.example",
            FromName = "Ä \"q\"",
            OriginalMessageId = "<id@x>\r\nX-Evil: 1",
            OriginalSubject = "Hi\r\nBcc: evil@x",
        };
        var raw = MdnMessage.Build(o, Now(), "t");
        var head = raw.Split("\r\n\r\n")[0];
        Assert.DoesNotContain(head.Split('\n'), l => l.StartsWith("Bcc:", StringComparison.Ordinal) || l.StartsWith("X-Evil", StringComparison.Ordinal));
        Assert.Contains("To: a@b.example" + "Bcc:victim@x", head, StringComparison.Ordinal);
        Assert.Contains("Subject: Read: Hi Bcc: evil@x", head, StringComparison.Ordinal);
        Assert.Contains("From: =?UTF-8?B?", head, StringComparison.Ordinal);
        Assert.Equal("\"Smith, John\"", MdnMessage.DisplayName("Smith, John"));
        Assert.Equal("=?UTF-8?B?R3LDvMOfZQ==?=", MdnMessage.EncodeHeaderWord("Grüße"));
    }

    [Fact]
    [Red("reader")]
    [RustTest("mdn::base64_wraps_at_76")]
    public void Base64_wraps_at_76()
    {
        var b = MdnMessage.Base64Body(new string('x', 200));
        var lines = b.Split("\r\n");
        Assert.All(lines, l => Assert.True(l.Length <= 76));
        Assert.Equal(4, lines.Length);
    }

    [Fact]
    [Red("reader")]
    [RustTest("mdn::requester_and_offer_rules")]
    public void Requester_and_offer_rules()
    {
        var h = Arr("""[{"name": "Subject", "value": " x"}, {"name": "disposition-notification-to", "value": " Anna <anna@example.com>"}]""");
        Assert.Equal("anna@example.com", MdnMessage.RequestedBy(h));
        Assert.Equal("bob@example.com", MdnMessage.RequestedBy(Arr("""[{"name": "Disposition-Notification-To", "value": " bob@example.com"}]""")));
        Assert.Null(MdnMessage.RequestedBy(Arr("""[{"name": "Disposition-Notification-To", "value": " nobody"}]""")));

        var m = new JsonObject { ["headers"] = h.DeepClone(), ["keywords"] = new JsonObject() };
        Assert.True(MdnMessage.ShouldOffer(m, ["inbox"], "ask"));
        Assert.False(MdnMessage.ShouldOffer(m, ["inbox"], "never"));
        Assert.False(MdnMessage.ShouldOffer(m, ["junk"], "ask"));
        Assert.False(MdnMessage.ShouldOffer(m, ["sent"], "always"));
        var done = new JsonObject { ["headers"] = h.DeepClone(), ["keywords"] = J("""{"$mdnsent": true}""") };
        Assert.False(MdnMessage.ShouldOffer(done, ["inbox"], "ask"));
    }

    [Fact]
    [Red("reader")]
    [RustTest("mdn::identity_follows_the_recipient")]
    public void Identity_follows_the_recipient()
    {
        IReadOnlyList<JsonNode> ids = [J("""{"id": "1", "email": "main@x.example"}"""), J("""{"id": "2", "email": "Alias@x.example"}""")];
        var m = J("""{"to": [{"email": "alias@x.example"}]}""");
        Assert.Equal("2", (string)MdnMessage.PickIdentity(ids, m)!["id"]!);
        Assert.Equal("1", (string)MdnMessage.PickIdentity(ids, J("""{"to": [{"email": "list@y"}]}"""))!["id"]!);
    }
}
