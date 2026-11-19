// Mirrors: spec/menus.md MENU-READER and MENU-ATT rows; Rust reader.rs save_eml :803, download_all :824, the source banner :252; Bulwark components/email/view-source-dialog.tsx,
// reader-attachments.tsx. The commands behind "View source", "Download (.eml)", "Download all attachments (.zip)", "Save as…" and "Copy file name"; they run on the
// message in Targets (the reader's menu and the list's "More actions" both pass it). I-183, I-200, I-226, I-227, I-288. Owner reader.

using System.Text;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Mail.Attachments;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Reader;
using Barbican.Core.Storage;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Common;
using Barbican.Presentation.Dialogs;
using Barbican.Presentation.Files;

namespace Barbican.Presentation.Reader;

/// <summary>The reader and attachment commands.</summary>
public sealed class ReaderCommands(IAttachmentSaver saver, Func<EmailBody?> open, IClipboardService clipboard, IDialogService dialogs, IToastService? toasts = null, IPrefsStore? prefs = null)
{
    /// <summary>How many commands <see cref="Create"/> returns (the registration adds one per index).</summary>
    public const int Count = 6;

    /// <summary>The commands: view source, download .eml, download all (.zip), save one attachment, copy an attachment's name.</summary>
    public IReadOnlyList<IAppCommand> Create() =>
    [
        new DelegateAppCommand(CommandIds.ViewSource, One, ViewSourceAsync),
        new DelegateAppCommand(CommandIds.DownloadEml, One, async (c, ct) => Saved(await saver.SaveEmlAsync(c.Targets[0], ct).ConfigureAwait(false))),
        new DelegateAppCommand(CommandIds.DownloadAllZip, One, async (c, ct) => Saved(await saver.SaveAllAsZipAsync(c.Targets[0], ct).ConfigureAwait(false))),
        new DelegateAppCommand(CommandIds.SaveAttachment, c => One(c) && Pick(c) is { Attachment.BlobId: not null }, SaveAttachmentAsync),
        new DelegateAppCommand(CommandIds.OpenAttachment, c => One(c) && Pick(c) is { Attachment.BlobId: not null }, PreviewAsync),
        new DelegateAppCommand(CommandIds.CopyAttachmentName, c => Pick(c) is not null, (c, _) =>
        {
            if (Pick(c) is not { } attachment) return Task.FromResult(Result<Unit>.Fail(new ValidationError("The attachment is no longer open.")));
            clipboard.SetText(attachment.Attachment.Name ?? "attachment");
            return Task.FromResult(Result<Unit>.Ok(default));
        }),
    ];

    /// <summary>The open reader's account-scoped attachment command context.</summary>
    public static CommandContext AttachmentContext(string accountKey, EmailBody body, int index) =>
        new(CommandSurface.Toolbar, [MailTarget.OfOpen(accountKey, body)], null, index.ToString(System.Globalization.CultureInfo.InvariantCulture), accountKey);

    private static bool One(CommandContext c) => c.Targets.Count == 1;

    private AttachmentRef? Pick(CommandContext c) =>
        c.Targets.Count == 1 && open() is { } body && string.Equals(c.Targets[0].EmailId, body.Id, StringComparison.Ordinal)
            && int.TryParse(c.Argument, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var i) && i >= 0 && i < body.Attachments.Count
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

    private async Task<Result<Unit>> SaveAttachmentAsync(CommandContext context, CancellationToken ct)
    {
        if (Pick(context) is not { Attachment.BlobId: not null } attachment)
            return Result<Unit>.Fail(new ValidationError("The attachment is no longer open."));
        return Saved(await saver.SaveAsync(context.Targets[0], attachment, ct).ConfigureAwait(false));
    }

    // Preview: the attachment is downloaded under the preview cap and shown in a dialog (an image, text or .eml); a plugin may take it over (onAttachmentPreview); I-228
    private async Task<Result<Unit>> PreviewAsync(CommandContext c, CancellationToken ct)
    {
        if (Pick(c) is not { Attachment.BlobId: not null } pick)
            return Result<Unit>.Fail(new ValidationError("The attachment is no longer open."));
        var target = c.Targets[0];
        var kind = FilesPreviewRules.KindOf(pick.Attachment.Type, pick.Attachment.Name ?? string.Empty);
        if (string.Equals(prefs?.Current.Str("mailAttachmentAction", "preview"), "download", StringComparison.Ordinal)
            || kind is not (FilesPreviewKind.Image or FilesPreviewKind.Text or FilesPreviewKind.Eml)
            || pick.Attachment.Size > AttachmentSaver.MaxPreviewBytes)
        {
            return Saved(await saver.SaveAsync(target, pick, ct).ConfigureAwait(false));
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
