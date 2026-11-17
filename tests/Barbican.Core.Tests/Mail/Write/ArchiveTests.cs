// Archive by archiveMode (Rust mail.rs:1064 archive_path, :1074 archive_dated; Bulwark client.ts archiveEmails :4215): the folder path under Archive,
// and the dated folders created on first use through MailWriter against the fake server.

using Barbican.Core.Mail.Archive;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Write;
using Barbican.Core.Tests.Jmap;

namespace Barbican.Core.Tests.Mail.Write;

public sealed class ArchiveTests
{
    private static readonly DateTimeOffset Received = new(2026, 3, 9, 23, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Path_follows_the_mode_in_local_time()
    {
        Assert.Empty(ArchivePath.Of(Received, "single", TimeZoneInfo.Utc));
        Assert.Empty(ArchivePath.Of(Received, "nonsense", TimeZoneInfo.Utc));
        Assert.Equal(["2026"], ArchivePath.Of(Received, "year", TimeZoneInfo.Utc));
        Assert.Equal(["2026", "03"], ArchivePath.Of(Received, "month", TimeZoneInfo.Utc));

        // 23:30 UTC on 31 December is already the new year in Tokyo
        var nye = new DateTimeOffset(2026, 12, 31, 23, 30, 0, TimeSpan.Zero);
        var tokyo = TimeZoneInfo.CreateCustomTimeZone("t", TimeSpan.FromHours(9), "t", "t");
        Assert.Equal(["2027", "01"], ArchivePath.Of(nye, "month", tokyo));
    }

    [Fact]
    public async Task Ensure_creates_the_missing_year_and_month_once()
    {
        var f = await Fake.StartAsync();
        var writer = new MailWriter(f.Client, f.Server.Options.AccountId);
        var store = f.Server.Store;
        var archive = store.MailboxId("archive")!;
        List<Mailbox> boxes = [.. store.Mailboxes().Select(m => new Mailbox { Id = (string)m["id"]!, Name = (string)m["name"]!, ParentId = (string?)m["parentId"] })];

        var first = await ArchiveFolders.EnsureAsync(writer, boxes, archive, ["2026", "03"], TestContext.Current.CancellationToken);
        Assert.True(first.IsOk, first.Error?.Message);
        var created = store.Mailboxes().Count;

        // the same month again: found, nothing created
        var again = await ArchiveFolders.EnsureAsync(writer, boxes, archive, ["2026", "03"], TestContext.Current.CancellationToken);
        Assert.Equal(first.Value, again.Value);
        Assert.Equal(created, store.Mailboxes().Count);

        var month = store.Mailboxes().First(m => string.Equals((string?)m["id"], first.Value, StringComparison.Ordinal));
        Assert.Equal("03", (string?)month["name"]);
        var year = store.Mailboxes().First(m => string.Equals((string?)m["id"], (string?)month["parentId"], StringComparison.Ordinal));
        Assert.Equal("2026", (string?)year["name"]);
        Assert.Equal(archive, (string?)year["parentId"]);
    }
}
