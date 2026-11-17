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
