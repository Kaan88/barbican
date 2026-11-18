// Mirrors: Bulwark lib/plugin-catalog onBeforeFileUpload { name, size, type, path } and onFileUploadCancel { name, path } (lib/plugin-hook-catalog.ts:217, :219). Owner files-data.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>onBeforeFileUpload: what is about to be uploaded and into which folder path.</summary>
public sealed record FileUploadPayload(string Name, long Size, string Type, string Path);
