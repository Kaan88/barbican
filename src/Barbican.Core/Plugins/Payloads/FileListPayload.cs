// Mirrors: Bulwark lib/plugin-hook-catalog.ts:222-:223 { files: FileResourceView[] } (onBeforeFileDelete, onAfterFileDelete). Owner files-data.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>onBeforeFileDelete and onAfterFileDelete.</summary>
public sealed record FileListPayload(IReadOnlyList<FileResourceView> Files);
