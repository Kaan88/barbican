// Mirrors: spec/menus.md MENU-READER and MENU-ATT rows; Rust reader.rs save_eml :803, download_all :824, the source banner :252; Bulwark components/email/view-source-dialog.tsx,
// reader-attachments.tsx. The commands behind "View source", "Download (.eml)", "Download all attachments (.zip)", "Save as…" and "Copy file name"; they run on the
// message in Targets (the reader's menu and the list's "More actions" both pass it). I-183, I-200, I-226, I-227, I-288. Owner reader.

using System.Text;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Mail.Attachments;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Reader;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Common;
using Barbican.Presentation.Dialogs;
using Barbican.Presentation.Files;

namespace Barbican.Presentation.Reader;

/// <summary>The reader and attachment commands.</summary>
public sealed class ReaderCommands(AttachmentSaver saver, Func<EmailBody?> open, IClipboardService clipboard, IDialogService dialogs, IToastService? toasts = null)
{
    /// <summary>How many commands <see cref="Create"/> returns (the registration adds one per index).</summary>
    public const int Count = 6;

    /// <summary>The commands: view source, download .eml, download all (.zip), save one attachment, copy an attachment's name.</summary>
    public IReadOnlyList<IAppCommand> Create() =>
    [
        new DelegateAppCommand(CommandIds.ViewSource, One, ViewSourceAsync),
        new DelegateAppCommand(CommandIds.DownloadEml, One, async (c, ct) => Saved(await saver.SaveEmlAsync(c.Targets[0], ct).ConfigureAwait(false))),
        new DelegateAppCommand(CommandIds.DownloadAllZip, One, async (c, ct) => Saved(await saver.SaveAllAsZipAsync(c.Targets[0], ct).ConfigureAwait(false))),
        new DelegateAppCommand(CommandIds.SaveAttachment, c => One(c) && Pick(c) is { Attachment.BlobId: not null }, async (c, ct) => Saved(await saver.SaveAsync(c.Targets[0], Pick(c)!, ct).ConfigureAwait(false))),
        new DelegateAppCommand(CommandIds.OpenAttachment, c => One(c) && Pick(c) is { Attachment.BlobId: not null }, PreviewAsync),
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

    // Preview: the attachment is downloaded under the preview cap and shown in a dialog (an image, text or .eml); a plugin may take it over (onAttachmentPreview); I-228
    private async Task<Result<Unit>> PreviewAsync(CommandContext c, CancellationToken ct)
    {
        var target = c.Targets[0];
        var pick = Pick(c)!;
        var kind = FilesPreviewRules.KindOf(pick.Attachment.Type, pick.Attachment.Name ?? string.Empty);
        if (kind is not (FilesPreviewKind.Image or FilesPreviewKind.Text or FilesPreviewKind.Eml))
        {
            return Result<Unit>.Fail(new ValidationError("This kind of file is not shown in the app; save it instead."));
        }

        var bytes = await saver.ReadForPreviewAsync(target, pick, ct).ConfigureAwait(false);
        if (!bytes.IsOk)
        {
            return Result<Unit>.Fail(bytes.Error!);
        }

        if (bytes.Value is { } content)
        {
            _ = await dialogs.ShowAsync(new AttachmentPreviewDialogRequest(pick.Attachment.Name ?? "attachment", kind, content, async token => _ = Saved(await saver.SaveAsync(target, pick, token).ConfigureAwait(false))), ct).ConfigureAwait(false);
        }

        return Result<Unit>.Ok(default);
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
