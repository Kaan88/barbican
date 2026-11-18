// Mirrors: Rust files.rs:355 download, :387 upload, :546 upload_folder; Bulwark stores/files-store.ts (upload, download), lib/files-zip.ts (zip download), components/files/new-file-dialog.tsx, upload-conflict-dialog.tsx, lib/download-filename.ts, lib/files-upload-conflicts.ts;
// JMAP blob upload (RFC 8620 6.1, Blob/upload RFC 9404) then FileNode/set with blobId; Stalwart crates/jmap/src/file/set.rs. G-016..G-019. Hooks: onBefore/AfterFileUpload, onFileUploadCancel, onFileDownload. Contract from the M5 plan section 4.9; owner files-data.

using Barbican.Core.Common;

namespace Barbican.Core.Files;

/// <summary>Uploads and downloads. Streams are read from and written to paths the user picked through the system picker; nothing here reads outside them.</summary>
public interface IFileTransfer
{
    /// <summary>Uploads local files into a folder (null: the root); <paramref name="onConflict"/> is asked once per taken name; progress is 0 to 1 over all bytes; cancellation fires onFileUploadCancel and leaves no half-made node. Returns the keys created.</summary>
    Task<Result<IReadOnlyList<string>>> UploadFilesAsync(string accountKey, string? parentKey, IReadOnlyList<string> paths, Func<string, Task<FileConflictChoice>> onConflict, IProgress<double>? progress, CancellationToken ct);

    /// <summary>Uploads a local folder with its structure (folders first, then the files).</summary>
    Task<Result<IReadOnlyList<string>>> UploadFolderAsync(string accountKey, string? parentKey, string path, Func<string, Task<FileConflictChoice>> onConflict, IProgress<double>? progress, CancellationToken ct);

    /// <summary>Downloads one file to a path (the name goes through the download filename rules of settings: space replacement, case, diacritics, separators).</summary>
    Task<Result<string>> DownloadAsync(string accountKey, string key, string directory, CancellationToken ct);

    /// <summary>Several files and folders as one zip (folders recursive); returns the path written.</summary>
    Task<Result<string>> DownloadZipAsync(string accountKey, IReadOnlyList<string> keys, string directory, CancellationToken ct);

    /// <summary>A new empty text file (or with the given text) in a folder; returns the key.</summary>
    Task<Result<string>> CreateTextFileAsync(string accountKey, string? parentKey, string name, string text, CancellationToken ct);

    /// <summary>The bytes of a file for the in-app preview (images, text, pdf; refused above <paramref name="maxBytes"/>).</summary>
    Task<Result<byte[]>> ReadAsync(string accountKey, string key, int maxBytes, CancellationToken ct);
}
