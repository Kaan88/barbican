// Mirrors: Bulwark lib/plugin-hook-catalog.ts:219 onFileUploadCancel { name, path }. Owner files-data.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>onFileUploadCancel: the file being uploaded when the user cancelled, and the folder path.</summary>
public sealed record FileCancelPayload(string Name, string Path);
