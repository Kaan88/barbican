// New tests (spec G-046; Bulwark lib/mail-export/): a folder saved as mbox reads back through the importer's reader with the same messages,
// read state and flags, and a "From " body line survives (mboxrd quoting); saved as .eml it gives one file per message. Owner unified.

using System.Text;
using Barbican.Core.Mail.Import;
using Barbican.Core.Mail.Model;
using Barbican.FakeJmap;

namespace Barbican.Core.Tests.Mail.Import;

public sealed class MailExportServiceTests : IDisposable
{
    private readonly FakeJmapServer _server = new(new FakeJmapOptions(), seed: false);
    private readonly string _folder;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "barbican-export-" + Guid.NewGuid().ToString("N"));

    public MailExportServiceTests()
    {
        // the fake server's message blob is the Text as given, so the text is a whole message here
        _folder = _server.Store.AddMailbox("Projects", null);
        _server.Store.Deliver(new FakeMail { Subject = "first", Text = "Subject: first\r\nMessage-ID: <first@example.org>\r\n\r\nplain body\r\n", Keywords = ["$seen"], ReceivedAt = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero) }, _folder);
        _server.Store.Deliver(new FakeMail { Subject = "second", Text = "Subject: second\r\nMessage-ID: <second@example.org>\r\n\r\nFrom the start of a line\r\nend\r\n", Keywords = ["$flagged"], ReceivedAt = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero) }, _folder);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _server.Dispose();
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private async Task<(MailExportService Service, MailboxRef Folder)> ServiceAsync()
    {
        var client = await FakeJmapHarness.ConnectAsync(_server, TimeProvider.System, ct: Ct);
        return (new MailExportService(_ => client), new MailboxRef("key", client.Session.MailAccountId, _folder, null));
    }

    [Fact]
    public async Task A_folder_saved_as_mbox_reads_back_with_its_messages_and_states()
    {
        var (service, folder) = await ServiceAsync();
        using var mbox = new MemoryStream();

        var r = await service.ExportMboxAsync(folder, mbox, null, Ct);

        Assert.True(r.IsOk, r.Error?.Message);
        Assert.Equal(2, r.Value);
        Assert.Contains("\n>From the start of a line\n", Encoding.UTF8.GetString(mbox.ToArray()), StringComparison.Ordinal);
        mbox.Position = 0;
        var reader = new MboxReader("folder.mbox", mbox, MboxReader.MaxEmlBytes);
        var items = new[] { reader.NextItem()!, reader.NextItem()! };
        Assert.Null(reader.NextItem());

        // the fake server ignores the sort, so the messages are told apart by subject
        var first = items.Single(i => Encoding.UTF8.GetString(i.Bytes).Contains("Subject: first", StringComparison.Ordinal));
        var second = items.Single(i => Encoding.UTF8.GetString(i.Bytes).Contains("Subject: second", StringComparison.Ordinal));
        Assert.Contains("$seen", first.Keywords, StringComparer.Ordinal);
        Assert.Contains("$flagged", second.Keywords, StringComparer.Ordinal);
        Assert.DoesNotContain("$seen", second.Keywords, StringComparer.Ordinal);
        Assert.Contains("\nFrom the start of a line", Encoding.UTF8.GetString(second.Bytes).Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_folder_saved_as_eml_gives_one_file_per_message()
    {
        var (service, folder) = await ServiceAsync();

        var r = await service.ExportEmlAsync(folder, _dir, null, Ct);

        Assert.Equal(2, r.Value);
        Assert.Equal(["first.eml", "second.eml"], Directory.GetFiles(_dir).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList());
    }
}
