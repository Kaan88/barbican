// Mirrors: spec/menus.md MENU-FOLDER "Import messages (.eml, .mbox)…" and "Export as mbox… / as .eml files…"; Rust mailimport.rs:281 import_files and :381 summary (the toast
// after an import); Bulwark components/mail-import and lib/mail-export (a folder saved as mbox or .eml files). I-065, I-287, G-046. Owner unified.
// An import is a write: it runs only on an account that may write (the same gate as the mail actions). An export only reads.

using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Menus;

namespace Barbican.Presentation.Unified;

/// <summary>The commands of the folder menu's import and export rows.</summary>
public static class TransferCommands
{
    /// <summary>How many commands <see cref="Create"/> returns (the registration adds one per index).</summary>
    public const int Count = 3;

    /// <summary>Import into the folder, export it as one mbox file, export it as .eml files.</summary>
    public static IReadOnlyList<IAppCommand> Create(MailTransfer transfer, IMailWriterSource writers, IMenuDataSource folders, IToastService? toasts = null)
    {
        ArgumentNullException.ThrowIfNull(transfer);
        ArgumentNullException.ThrowIfNull(writers);
        ArgumentNullException.ThrowIfNull(folders);
        return
        [
            new DelegateAppCommand(
                CommandIds.ImportMessages,
                c => c.Mailbox is { } m && !transfer.IsRunning && writers.CanWrite(m.AccountKey),
                async (c, ct) =>
                {
                    var r = await transfer.ImportAsync(c.Mailbox!, ct).ConfigureAwait(true);
                    return Done(r is { IsOk: false } failed ? failed.Error : null, r is not null, transfer, toasts);
                }),
            Export(CommandIds.ExportMbox, asMbox: true, transfer, folders, toasts),
            Export(CommandIds.ExportEml, asMbox: false, transfer, folders, toasts),
        ];
    }

    private static DelegateAppCommand Export(string id, bool asMbox, MailTransfer transfer, IMenuDataSource folders, IToastService? toasts) =>
        new(
            id,
            c => c.Mailbox is not null && !transfer.IsRunning,
            async (c, ct) =>
            {
                var name = folders.Folder(c.Mailbox!.AccountKey, c.Mailbox.MailboxId)?.Name ?? "messages";
                var r = await transfer.ExportAsync(c.Mailbox, name, asMbox, ct).ConfigureAwait(true);
                return Done(r is { IsOk: false } failed ? failed.Error : null, r is not null, transfer, toasts);
            });

    // a cancelled or unanswered pick ends quietly; otherwise the transfer's own summary is the toast
    private static Result<Unit> Done(JmapError? error, bool ran, MailTransfer transfer, IToastService? toasts)
    {
        if (error is not null)
        {
            return Result<Unit>.Fail(error);
        }

        if (ran && transfer.Summary.Length > 0)
        {
            toasts?.Show(new ToastRequest(transfer.Summary));
        }

        return Result<Unit>.Ok(default);
    }
}
