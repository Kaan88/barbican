// Mirrors: Bulwark components/email/rename-attachment-dialog.tsx:21-43 (only the part before the extension is edited, so the file keeps its type; the
// extension is shown after the field and checked with the value; cancelled answers null); ui/prompt-dialog.tsx:21-35,82-95 (trimmed value, error under
// the field, confirm disabled while empty or invalid); locales/en common.json:1296-1302 (Rename attachment, Rename, the two messages). Owner reader.

using Barbican.Presentation.Dialogs;

namespace Barbican.Presentation.Reader;

/// <summary>The rename dialog of one attachment: the stem to edit and the extension that stays fixed.</summary>
/// <param name="Stem">The part before the extension, as the field starts.</param>
/// <param name="Extension">The extension the file keeps, shown after the field (empty for a name without one).</param>
public sealed record RenameAttachmentDialogRequest(string Stem, string Extension) : DialogRequest<string>("Rename attachment");
