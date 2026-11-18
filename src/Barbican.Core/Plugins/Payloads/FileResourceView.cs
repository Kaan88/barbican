// Mirrors: Bulwark lib/plugin-types.ts:719 FileResourceView, lib/plugin-projection.ts:199 fileToView (the payload of onAfterFileUpload, onFileDownload, onDirectoryCreate, onFileDuplicate and the lists of the delete, move and copy hooks). Owner files-data.

using Barbican.Core.Files;

namespace Barbican.Core.Plugins.Payloads;

/// <summary>A file or folder as a plugin sees it: type is "file" or "directory", path is the folder it is in.</summary>
public sealed record FileResourceView(string Id, string Name, string Type, long Size, string MimeType, string Path, string? Modified)
{
    /// <summary>The view of an entry; <paramref name="path"/> is the folder holding it.</summary>
    public static FileResourceView From(FileEntry entry, string path)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(path);
        return new FileResourceView(entry.Key, entry.Name, entry.IsDirectory ? "directory" : "file", entry.Size, entry.IsDirectory ? string.Empty : entry.MediaType ?? "application/octet-stream", path, entry.Modified?.ToString("o", System.Globalization.CultureInfo.InvariantCulture));
    }
}
