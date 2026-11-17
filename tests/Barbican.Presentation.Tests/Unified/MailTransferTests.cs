// New tests (M2 plan 6.5: import with progress and cancel, pickers with queued answers; spec I-065 Import/Export part). The folder menu's
// "Import messages…" goes through MailTransfer: a queued pick answers the file, the summary reads as Rust mailimport.rs:381, an unanswered
// pick does nothing, and Cancel stops the run. Owner unified.

using Barbican.Core.Common;
using Barbican.Core.Mail.Import;
using Barbican.Core.Mail.Model;
using Barbican.Presentation.Common;
using Barbican.Presentation.Unified;

namespace Barbican.Presentation.Tests.Unified;

public sealed class MailTransferTests
{
    private static readonly MailboxRef Inbox = new("key", "a1", "m1", "inbox");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (MailTransfer Transfer, TransferPicks Picks) Make(IMailImport import)
    {
        var picks = new TransferPicks();
        return (new MailTransfer(import, new MailExportService(_ => null), picks, new InlineDispatcher()), picks);
    }

    [Fact]
    public async Task A_queued_pick_imports_the_file_and_sums_up()
    {
        var import = new StubImport((_, paths, progress, _) =>
        {
            progress.Report(3);
            return Task.FromResult(Result<ImportOutcome>.Ok(new ImportOutcome(2, 1, [])));
        });
        var (transfer, picks) = Make(import);
        picks.Queue("C:/mail/old.mbox");

        var r = await transfer.ImportAsync(Inbox, Ct);

        Assert.Equal(["C:/mail/old.mbox"], import.Paths);
        Assert.Equal(2, r!.Value.Value!.Imported);
        Assert.Equal("Imported 2 messages, 1 already in the folder", transfer.Summary);
        Assert.Equal(3, transfer.Done);
        Assert.False(transfer.IsRunning);
    }

    [Fact]
    public async Task Without_an_answer_nothing_is_imported()
    {
        var import = new StubImport((_, _, _, _) => throw new InvalidOperationException("not called"));
        var (transfer, _) = Make(import);

        Assert.Null(await transfer.ImportAsync(Inbox, Ct));
        Assert.Null(import.Paths);
    }

    [Fact]
    public async Task Cancel_stops_the_running_import()
    {
        var started = new TaskCompletionSource();
        var import = new StubImport(async (_, _, progress, ct) =>
        {
            progress.Report(1);
            started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return Result<ImportOutcome>.Ok(new ImportOutcome(0, 0, []));
        });
        var (transfer, picks) = Make(import);
        picks.Queue("C:/mail/big.mbox");

        var run = transfer.ImportAsync(Inbox, Ct);
        await started.Task.WaitAsync(Ct);
        Assert.True(transfer.IsRunning);
        transfer.Cancel();

        Assert.Null(await run);
        Assert.False(transfer.IsRunning);
        Assert.StartsWith("Import stopped after", transfer.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_file_error_ends_the_import_with_its_reason_and_the_next_one_can_run()
    {
        // Fable review 3 R3-05: a full disk or a file in use must not leave the transfer "running" until a restart
        var calls = 0;
        var import = new StubImport((_, _, progress, _) =>
        {
            if (++calls == 1)
            {
                progress.Report(4);
                throw new IOException("The file is in use.");
            }

            return Task.FromResult(Result<ImportOutcome>.Ok(new ImportOutcome(1, 0, [])));
        });
        var (transfer, picks) = Make(import);
        picks.Queue("C:/mail/locked.mbox");

        var r = await transfer.ImportAsync(Inbox, Ct);

        Assert.False(r!.Value.IsOk);
        Assert.False(transfer.IsRunning);
        Assert.Equal("Import failed after 4 messages: The file is in use.", transfer.Summary);
        Assert.Contains("The file is in use.", r.Value.Error!.Message, StringComparison.Ordinal);

        picks.Queue("C:/mail/next.mbox");
        var next = await transfer.ImportAsync(Inbox, Ct);
        Assert.True(next!.Value.IsOk);
    }

    [Fact]
    public async Task Any_other_exception_still_ends_the_run()
    {
        var import = new StubImport((_, _, _, _) => throw new InvalidOperationException("a bug"));
        var (transfer, picks) = Make(import);
        picks.Queue("C:/mail/old.mbox");

        await Assert.ThrowsAsync<InvalidOperationException>(() => transfer.ImportAsync(Inbox, Ct));
        Assert.False(transfer.IsRunning);
        Assert.Equal("Import failed.", transfer.Summary);
    }

    [Fact]
    public async Task An_export_to_a_folder_that_went_away_fails_with_its_reason()
    {
        var import = new StubImport((_, _, _, _) => throw new InvalidOperationException("not called"));
        var (transfer, picks) = Make(import);
        var gone = Path.Combine(Path.GetTempPath(), "barbican-gone-" + Guid.NewGuid().ToString("N"), "missing", "inbox.mbox");
        picks.Queue(gone);

        var r = await transfer.ExportAsync(Inbox, "inbox", asMbox: true, Ct);

        Assert.False(r!.Value.IsOk);
        Assert.False(transfer.IsRunning);
        Assert.StartsWith("Saving failed after 0 messages: ", transfer.Summary, StringComparison.Ordinal);
    }

    private sealed class StubImport(Func<MailboxRef, IReadOnlyList<string>, IProgress<int>, CancellationToken, Task<Result<ImportOutcome>>> run) : IMailImport
    {
        public IReadOnlyList<string>? Paths { get; private set; }

        public Task<Result<ImportOutcome>> ImportFilesAsync(MailboxRef target, IReadOnlyList<string> paths, IProgress<int> progress, CancellationToken ct)
        {
            Paths = paths;
            return run(target, paths, progress, ct);
        }
    }
}
