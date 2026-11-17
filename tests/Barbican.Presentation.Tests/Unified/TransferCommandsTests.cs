// MENU-FOLDER "Import messages…" and "Export as…" (I-065, I-287): the import runs only where the account may write, a queued pick answers the file, the summary is the toast, and
// an unanswered pick ends quietly. Owner unified.

using Barbican.Core.Common;
using Barbican.Core.Mail.Import;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Write;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Common;
using Barbican.Presentation.Tests.Menus;
using Barbican.Presentation.Unified;

namespace Barbican.Presentation.Tests.Unified;

public sealed class TransferCommandsTests
{
    private static readonly MailboxRef Inbox = new("acct", "a1", "inbox", "inbox");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static CommandContext Context() => new(CommandSurface.ContextMenu, [], Inbox, null, "acct");

    private static (IReadOnlyList<IAppCommand> Commands, TransferPicks Picks, List<ToastRequest> Toasts) Make(bool canWrite)
    {
        var picks = new TransferPicks();
        var transfer = new MailTransfer(new Import(), new MailExportService(_ => null), picks, new InlineDispatcher());
        var toasts = new List<ToastRequest>();
        return (TransferCommands.Create(transfer, new Writers(canWrite), new FakeMenuData(), new Toasts(toasts)), picks, toasts);
    }

    [Fact]
    public void Import_is_off_on_an_account_that_cannot_write_and_export_stays_on()
    {
        var (readOnly, _, _) = Make(canWrite: false);
        Assert.False(readOnly.Single(c => string.Equals(c.Id, CommandIds.ImportMessages, StringComparison.Ordinal)).CanExecute(Context()));
        Assert.True(readOnly.Single(c => string.Equals(c.Id, CommandIds.ExportMbox, StringComparison.Ordinal)).CanExecute(Context()));
        Assert.True(readOnly.Single(c => string.Equals(c.Id, CommandIds.ExportEml, StringComparison.Ordinal)).CanExecute(Context()));

        var (writable, _, _) = Make(canWrite: true);
        Assert.True(writable.Single(c => string.Equals(c.Id, CommandIds.ImportMessages, StringComparison.Ordinal)).CanExecute(Context()));
    }

    [Fact]
    public async Task A_queued_pick_imports_and_the_summary_is_the_toast()
    {
        var (commands, picks, toasts) = Make(canWrite: true);
        picks.Queue("C:/mail/old.mbox");
        var r = await commands.Single(c => string.Equals(c.Id, CommandIds.ImportMessages, StringComparison.Ordinal)).ExecuteAsync(Context(), Ct);
        Assert.True(r.IsOk);
        Assert.StartsWith("Imported 2 messages", Assert.Single(toasts).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unanswered_pick_ends_quietly()
    {
        var (commands, _, toasts) = Make(canWrite: true);
        var r = await commands.Single(c => string.Equals(c.Id, CommandIds.ImportMessages, StringComparison.Ordinal)).ExecuteAsync(Context(), Ct);
        Assert.True(r.IsOk);
        Assert.Empty(toasts);
    }

    private sealed class Import : IMailImport
    {
        public Task<Result<ImportOutcome>> ImportFilesAsync(MailboxRef target, IReadOnlyList<string> paths, IProgress<int> progress, CancellationToken ct) =>
            Task.FromResult(Result<ImportOutcome>.Ok(new ImportOutcome(2, 0, [])));
    }

    private sealed class Writers(bool canWrite) : IMailWriterSource
    {
        public IMailWriter? WriterOf(string accountKey) => null;

        public bool CanWrite(string accountKey) => canWrite;
    }

    private sealed class Toasts(List<ToastRequest> shown) : IToastService
    {
        public ToastHandle Show(ToastRequest toast)
        {
            shown.Add(toast);
            return default;
        }

        public void Dismiss(ToastHandle handle)
        {
        }
    }
}
