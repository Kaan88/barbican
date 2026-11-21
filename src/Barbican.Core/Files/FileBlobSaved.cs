// Mirrors: Bulwark lib/files-save.ts SaveToFilesResult.

namespace Barbican.Core.Files;

/// <summary>The nodes created, by key, and the names an add-on stopped.</summary>
public sealed record FileBlobSaved(IReadOnlyList<string> Keys, IReadOnlyList<string> Stopped);
