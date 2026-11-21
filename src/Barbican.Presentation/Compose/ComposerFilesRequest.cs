// Mirrors: Bulwark components/files/file-picker-dialog.tsx:46-316 (the whole picker inside the modal, "Attach from Files") and components/email/email-composer.tsx:6735-6741
// (the composer opens it and attaches what onPick returns). Drawn by the App ComposerFilesDialogContent through the shared dialog host. Owner composer-ui.

using Barbican.Core.Files;
using Barbican.Presentation.Dialogs;

namespace Barbican.Presentation.Compose;

/// <summary>The attach-from-Files dialog: its answer is the picked files, never a folder, or null when cancelled.</summary>
/// <param name="Picker">The listing, the current level, the search and the ticks the card draws.</param>
public sealed record ComposerFilesRequest(ComposerFilesPicker Picker) : DialogRequest<IReadOnlyList<FileEntry>?>("Attach from Files");
