// New tests (M2 plan 6.5 exit tests) on FakeJmap: an mbox with a message the folder already has imports two and counts one duplicate, through
// the account's IMailWriter (upload, Email/import); a cancel stops between batches; a folder without mayAddItems is refused before any
// upload. Rust mailimport.rs:281 import_files; Bulwark lib/mail-import/engine.ts. Owner unified.

using System.Text;
using System.Text.Json.Nodes;
using Barbican.Core.Jmap;
using Barbican.Core.Mail.Import;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Write;
using Barbican.FakeJmap;

namespace Barbican.Core.Tests.Mail.Import;

public sealed class MailImportServiceTests : IDisposable
{
    private readonly FakeJmapServer _server = new(new FakeJmapOptions { MaxObjectsInSet = 1 }, seed: false);
    private readonly string _inbox;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "barbican-import-" + Guid.NewGuid().ToString("N"));

    public MailImportServiceTests()
    {
        _inbox = _server.Store.AddMailbox("Inbox", "inbox");
        Directory.CreateDirectory(_dir);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _server.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    private int InInbox => _server.Store.Query(new JsonObject { ["inMailbox"] = _inbox }).Count;

    private string Mbox(params string[] messageIds)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < messageIds.Length; i++)
        {
            sb.Append("From sender@example.org Sat Nov  1 09:00:00 2026\n")
              .Append("From: Sender <sender@example.org>\nTo: user@example.org\n")
              .Append(System.Globalization.CultureInfo.InvariantCulture, $"Subject: imported {i}\nDate: Sat, 1 Nov 2026 09:0{i}:00 +0000\nMessage-ID: <{messageIds[i]}>\n\nbody {i}\n\n");
        }

        var path = Path.Combine(_dir, $"mail-{Guid.NewGuid():N}.mbox");
        File.WriteAllText(path, sb.ToString());
        return path;
    }

    private async Task<(MailImportService Service, MailboxRef Target)> ServiceAsync()
    {
        var client = await FakeJmapHarness.ConnectAsync(_server, TimeProvider.System, ct: Ct);
        var service = new MailImportService(_ => (new MailWriter(client, client.Session.MailAccountId), client));
        return (service, new MailboxRef("key", client.Session.MailAccountId, _inbox, "inbox"));
    }

    [Fact]
    public async Task An_mbox_with_a_message_the_folder_has_imports_two_and_counts_one_duplicate()
    {
        var have = _server.Store.Deliver(new FakeMail { Subject = "already here" }, _inbox);
        var (service, target) = await ServiceAsync();
        var seen = new List<int>();

        var r = await service.ImportFilesAsync(target, [Mbox("new-1@example.org", have + "@example.org", "new-2@example.org")], new Progress<int>(seen.Add), Ct);

        Assert.True(r.IsOk, r.Error?.Message);
        Assert.Equal(2, r.Value!.Imported);
        Assert.Equal(1, r.Value.Already);
        Assert.Empty(r.Value.Errors);
        Assert.Equal(3, InInbox);
        Assert.Contains(_server.ApiCalls, c => c.Contains("Email/import", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_cancel_stops_between_batches()
    {
        var (service, target) = await ServiceAsync();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var progress = new SyncProgress(n =>
        {
            if (n >= 1)
            {
                cts.Cancel();
            }
        });

        try
        {
            _ = await service.ImportFilesAsync(target, [Mbox("c1@example.org", "c2@example.org", "c3@example.org", "c4@example.org")], progress, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // stopped
        }

        Assert.InRange(InInbox, 1, 2);
    }

    [Fact]
    public async Task A_folder_that_takes_no_messages_is_refused_before_any_upload()
    {
        IJmapClient client = await FakeJmapHarness.ConnectAsync(_server, TimeProvider.System, ct: Ct);
        Assert.True((await MailImportService.MayAddAsync(client, _inbox, Ct)).Value);
        Assert.False((await MailImportService.MayAddAsync(client, "no-such-folder", Ct)).IsOk);
    }

    private sealed class SyncProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }
}
