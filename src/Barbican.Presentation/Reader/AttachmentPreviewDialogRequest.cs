// Mirrors: Bulwark components/email/reader-attachments.tsx and components/files/file-preview-modal.tsx (the attachment preview), I-228; D-M5-15 (pdf, audio and video are not shown in the app). Owner files-ui.

using Barbican.Presentation.Dialogs;
using Barbican.Presentation.Files;

namespace Barbican.Presentation.Reader;

/// <summary>The attachment preview dialog: the file's bytes (already fetched, under the preview cap) and what to do with them.</summary>
/// <param name="Name">The file name (the heading).</param>
/// <param name="Kind">Image, Text or Eml; the dialog draws nothing else.</param>
/// <param name="Bytes">The content.</param>
/// <param name="Save">Saves the attachment (the same save as the menu's Save as).</param>
public sealed record AttachmentPreviewDialogRequest(string Name, FilesPreviewKind Kind, byte[] Bytes, Func<CancellationToken, Task> Save) : DialogRequest<bool>(Name);
