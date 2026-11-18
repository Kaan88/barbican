// Mirrors: Bulwark lib/plugin-hook-catalog.ts:226-:228 { files, toPath } (onBeforeFileMove, onFileMove, onFileCopy). Owner files-data.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>onBeforeFileMove, onFileMove and onFileCopy: the files and the folder path they go to.</summary>
public sealed record FileMovePayload(IReadOnlyList<FileResourceView> Files, string ToPath);
