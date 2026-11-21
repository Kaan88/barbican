// Mirrors: Bulwark components/files/file-picker-dialog.tsx:137-183 (a folder row opens, a file row toggles a check and shows its size; a search hit adds where the file lives);
// components/email/email-composer.tsx:3182-3225 (a picked node is attached by its blob id). Owner composer-ui.

namespace Barbican.Presentation.Compose;

/// <summary>One row of the attach-from-Files picker: a folder to open or a file to tick, never a folder of the selection.</summary>
/// <param name="Key">The <see cref="Barbican.Core.Files.FileEntry.Key"/>, or <see cref="ComposerFilesPicker.SharedLocation"/> for "Shared with me".</param>
/// <param name="Name">The file or folder name.</param>
/// <param name="IsFolder">A folder row (opened), or a file row (ticked).</param>
/// <param name="Size">The file size in bytes (0 for a folder).</param>
/// <param name="Detail">Under the name: where a search hit lives ("Projects / Plans"), else empty.</param>
public sealed record ComposerFilesRow(string Key, string Name, bool IsFolder, long Size, string? Detail);
