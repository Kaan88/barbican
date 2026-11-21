// Mirrors: Bulwark components/email/email-composer.tsx:3182-3225 (Files nodes are already blobs: the same onBeforeAttachmentUpload and maxSizeAttachmentsPerEmail gates, then the node's blob id,
// no download and no upload; one toast when the message total turns a file away), :6467-6475 (the Attach menu's "Attach from Files", only when the server has Files),
// :6735-6741 (the picker's onPick). components/files/file-picker-dialog.tsx is the listing (ComposerFilesPicker). Owner composer-ui.

using Barbican.Core.Compose;
using Barbican.Core.Files;
using Barbican.Presentation.Actions;

namespace Barbican.Presentation.Compose;

public sealed partial class ComposerViewModel
{
    /// <summary>The server of this login has Files, so the Attach menu offers "Attach from Files" (Bulwark's filesEnabled &amp;&amp; supportsFiles, :6467).</summary>
    public bool FilesAvailable { get; init; }

    /// <summary>Opens the attach-from-Files picker over this login's nodes and attaches what was picked (nothing on Cancel).</summary>
    public async Task OpenFilesPickerAsync()
    {
        if (_closed || !FilesAvailable || _c.Files is not { } files || _c.Dialogs is not { } dialogs)
        {
            return;
        }

        var picker = new ComposerFilesPicker(files, AccountKey, _c.ShowHiddenFiles);
        var picked = await dialogs.ShowAsync(new ComposerFilesRequest(picker), CancellationToken.None).ConfigureAwait(true);
        if (picked is { Count: > 0 })
        {
            await AttachStoredFilesAsync(picked).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Attaches files that are already blobs on the server (a Files node) by their blob id: the uploader applies the same gates and plugin intercept as an upload,
    /// and each file becomes a ready chip; a file the server no longer holds is skipped, a refused one says why and adds nothing.
    /// </summary>
    public async Task AttachStoredFilesAsync(IReadOnlyList<FileEntry> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        foreach (var file in files)
        {
            if (file.BlobId is not { Length: > 0 } blob)
            {
                continue;
            }

            var wanted = new DraftAttachment
            {
                Name = file.Name,
                Type = string.IsNullOrWhiteSpace(file.MediaType) ? "application/octet-stream" : file.MediaType,
                Size = file.Size,
                BlobId = blob,
            };

            try
            {
                var res = await _c.Uploader.AttachExistingAsync(AccountKey, wanted, [.. Attachments.Where(a => a.State == AttachmentState.Done).Select(a => a.Attachment)], CancellationToken.None).ConfigureAwait(true);
                if (res.IsOk)
                {
                    Attachments.Add(new AttachmentItem(res.Value!, AttachmentState.Done, null));
                    OnPropertyChanged(nameof(HasPendingUploads));
                }
                else
                {
                    _ = _c.Toasts?.Show(new ToastRequest(res.Error!.Message, IsError: true));
                }
            }
#pragma warning disable CA1031 // an attach that throws is a refused attach
            catch (Exception e)
#pragma warning restore CA1031
            {
                _ = _c.Toasts?.Show(new ToastRequest(e.Message, IsError: true));
            }
        }
    }
}
