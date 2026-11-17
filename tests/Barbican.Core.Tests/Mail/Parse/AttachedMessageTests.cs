// New tests (M2 plan 6.5 exit tests; spec J-058, G-047): an attached message opens through Email/parse (RFC 8621 §4.9) on FakeJmap, and a
// winmail.dat expands to the files inside it with MimeKit's TNEF reader (MS-OXTNEF). Owner unified.

using System.Text;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Parse;
using Barbican.Core.Mail.Tnef;
using Barbican.Core.Mail.Write;
using Barbican.FakeJmap;

namespace Barbican.Core.Tests.Mail.Parse;

public sealed class AttachedMessageTests : IDisposable
{
    private readonly FakeJmapServer _server = new(new FakeJmapOptions(), seed: false);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _server.Dispose();

    private static MailTarget Target(Barbican.Core.Jmap.Transport.JmapClient client) =>
        new("key", client.Session.MailAccountId, "e1", "t1", "e1", new Dictionary<string, bool>(StringComparer.Ordinal), Keywords.None);

    [Fact]
    public async Task An_attached_message_opens_through_Email_parse()
    {
        var client = await FakeJmapHarness.ConnectAsync(_server, TimeProvider.System, ct: Ct);
        var eml = "From: Ana <ana@example.org>\r\nTo: ben@example.net\r\nSubject: The forwarded one\r\nDate: Sat, 1 Nov 2026 09:00:00 +0000\r\n\r\nHello from inside.\r\n"u8.ToArray();
        var blob = (await new MailWriter(client, client.Session.MailAccountId).UploadAsync(eml, "message/rfc822", Ct)).Value!;
        var attachment = new AttachmentRef(0, new Attachment { BlobId = blob.BlobId, Name = "forwarded.eml", Type = "message/rfc822", Size = eml.Length });
        Assert.True(EmailParseOpener.IsMessage(attachment.Attachment));

        var opener = new EmailParseOpener(_ => client);
        var r = await opener.OpenAsync(Target(client), attachment, Ct);

        Assert.True(r.IsOk, r.Error?.Message);
        Assert.Equal("The forwarded one", r.Value!.Subject);
        Assert.Contains(_server.ApiCalls, c => c.Contains("Email/parse", StringComparison.Ordinal));
        Assert.False((await opener.OpenAsync(Target(client), new AttachmentRef(0, new Attachment { BlobId = "nope" }), Ct)).IsOk);
    }

    [Fact]
    public void A_winmail_dat_expands_to_the_files_inside()
    {
        var tnef = FakeTnef.Build([("report.txt", Encoding.UTF8.GetBytes("quarterly numbers")), ("photo.bin", [1, 2, 3, 4])]);
        var expander = new TnefExpander(_ => null);
        Assert.True(expander.CanHandle(new AttachmentRef(0, new Attachment { Name = "winmail.dat", Type = "application/ms-tnef" })));
        Assert.False(expander.CanHandle(new AttachmentRef(0, new Attachment { Name = "a.pdf", Type = "application/pdf" })));

        var files = TnefExpander.Expand("blob1", tnef);

        Assert.Equal(["report.txt", "photo.bin"], files.Select(f => f.Attachment.Name).ToList());
        Assert.Equal("quarterly numbers", Encoding.UTF8.GetString(TnefExpander.OpenLocal(files[0].Attachment.BlobId!)!));
        Assert.Equal(4, files[1].Attachment.Size);
        Assert.Empty(TnefExpander.Expand("blob2", "not tnef at all"u8.ToArray()));
    }
}
