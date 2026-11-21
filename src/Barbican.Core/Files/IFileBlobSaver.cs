// Mirrors: Bulwark lib/files-save.ts saveBlobsToFiles (blobs already on the server are filed by blobId, no download or upload; from another account of the login the blob is copied into the Files account first, RFC 8620 6.3 Blob/copy;
// onBeforeFileUpload may stop one, onAfterFileUpload reports each). Stalwart crates/jmap/src/blob/copy.rs, file/set.rs.

using Barbican.Core.Common;

namespace Barbican.Core.Files;

/// <summary>Files blobs that are already on the server (mail attachments) into a folder of the user's Files.</summary>
public interface IFileBlobSaver
{
    /// <summary>Files each blob into <paramref name="parentKey"/> (null: the top of the user's own drive). A name that is taken becomes "name (2).ext" on the server. Stops at the first failure; what was filed before it stays and is listed in the partial result.</summary>
    Task<Result<FileBlobSaved>> SaveAsync(string accountKey, string? parentKey, string? blobAccountId, IReadOnlyList<FileBlobSource> blobs, CancellationToken ct);
}
