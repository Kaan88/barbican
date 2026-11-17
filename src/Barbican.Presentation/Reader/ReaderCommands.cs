// Mirrors: spec/menus.md MENU-READER and MENU-ATT rows; Rust reader.rs save_eml :803, download_all :824, the source banner :252; Bulwark components/email/view-source-dialog.tsx,
// reader-attachments.tsx. The commands behind "View source", "Download (.eml)", "Download all attachments (.zip)", "Save as…" and "Copy file name"; they run on the
// message in Targets (the reader's menu and the list's "More actions" both pass it). I-183, I-200, I-226, I-227, I-288. Owner reader.

using System.Text;
using Barbican.Core.Common;
using Barbican.Core.Mail.Attachments;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Reader;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Common;
using Barbican.Presentation.Dialogs;

namespace Barbican.Presentation.Reader;

/// <summary>The reader and attachment commands.</summary>
public sealed class ReaderCommands(AttachmentSaver saver, Func<EmailBody?> open, IClipboardService clipboard, IDialogService dialogs, IToastService? toasts = null)
{
    /// <summary>How many commands <see cref="Create"/> returns (the registration adds one per index).</summary>
    public const int Count = 5;

    /// <summary>The commands: view source, download .eml, download all (.zip), save one attachment, copy an attachment's name.</summary>
    public IReadOnlyList<IAppCommand> Create() =>
    [
        new DelegateAppCommand(CommandIds.ViewSource, One, ViewSourceAsync),
        new DelegateAppCommand(CommandIds.DownloadEml, One, async (c, ct) => Saved(await saver.SaveEmlAsync(c.Targets[0], ct).ConfigureAwait(false))),
        new DelegateAppCommand(CommandIds.DownloadAllZip, One, async (c, ct) => Saved(await saver.SaveAllAsZipAsync(c.Targets[0], ct).ConfigureAwait(false))),
        new DelegateAppCommand(CommandIds.SaveAttachment, c => One(c) && Pick(c) is { Attachment.BlobId: not null }, async (c, ct) => Saved(await saver.SaveAsync(c.Targets[0], Pick(c)!, ct).ConfigureAwait(false))),
        new DelegateAppCommand(CommandIds.CopyAttachmentName, c => Pick(c) is not null, (c, _) =>
        {
            clipboard.SetText(Pick(c)!.Attachment.Name ?? "attachment");
            return Task.FromResult(Result<Unit>.Ok(default));
        }),
    ];

    private static bool One(CommandContext c) => c.Targets.Count == 1;

    private AttachmentRef? Pick(CommandContext c) =>
        open() is { } body && int.TryParse(c.Argument, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var i) && i < body.Attachments.Count
            ? new AttachmentRef(i, body.Attachments[i])
            : null;

    private Result<Unit> Saved(Result<Unit> r)
    {
        if (r.IsOk)
        {
            toasts?.Show(new ToastRequest("Saved."));
        }

        return r;
    }

    private async Task<Result<Unit>> ViewSourceAsync(CommandContext c, CancellationToken ct)
    {
        var raw = await saver.RawAsync(c.Targets[0], ct).ConfigureAwait(false);
        if (!raw.IsOk)
        {
            return Result<Unit>.Fail(raw.Error!);
        }

        var (text, cut) = SourceCut.Shown(Encoding.UTF8.GetString(raw.Value.Bytes));
        var target = c.Targets[0];
        var request = new SourceDialogRequest("Message source", text, cut, clipboard.SetText, async token => _ = Saved(await saver.SaveEmlAsync(target, token).ConfigureAwait(false)));
        _ = await dialogs.ShowAsync(request, ct).ConfigureAwait(false);
        return Result<Unit>.Ok(default);
    }
}
