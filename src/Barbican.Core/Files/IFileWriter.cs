// Mirrors: Rust files.rs:409 new_folder, :451 rename, :465 delete (into the trash), :506 purge, :515 restore, :539 empty_trash, :619 move_to, :625 set_clipboard, :652 paste, :684 duplicate, :743 share; Bulwark stores/files-store.ts, lib/files-trash.ts, components/files/file-browser.tsx;
// draft-ietf-jmap-filenode FileNode/set (parentId, name, blobId, shareWith), copy for another account; Stalwart crates/jmap/src/file/{set,copy}.rs. B-FILE-002, B-FILE-006, B-FILE-007, B-FILE-009..011.
// Hooks: onDirectoryCreate, onBefore/AfterFileDelete, onBeforeFileRename, onFileRename, onBeforeFileMove, onFileMove, onFileCopy, onFileDuplicate. Contract from the M5 plan section 4.8; owner files-data.

using Barbican.Core.Common;

namespace Barbican.Core.Files;

/// <summary>Every FileNode write that is not a transfer. Each applies its hooks, writes through JMAP and leaves <see cref="IFileStore"/> consistent. Keys are <see cref="FileEntry.Key"/> values.</summary>
public interface IFileWriter
{
    /// <summary>A folder in <paramref name="parentKey"/> (null: the root); on a draft-18 server the node type is sent when the empty drive's supportedNodeTypes says so (B-FILE-002); a name already used is made unique.</summary>
    Task<Result<string>> CreateFolderAsync(string accountKey, string? parentKey, string name, CancellationToken ct);

    Task<Result<Unit>> RenameAsync(string accountKey, string key, string newName, CancellationToken ct);

    /// <summary>Moves into a folder of the same account (a different account is a copy).</summary>
    Task<Result<Unit>> MoveAsync(string accountKey, IReadOnlyList<string> keys, string? destinationKey, CancellationToken ct);

    /// <summary>Copy (the Ctrl+C then Ctrl+V of the clipboard, also across accounts: the blob is copied with Blob/copy) into a folder; names made unique.</summary>
    Task<Result<Unit>> CopyAsync(string accountKey, IReadOnlyList<string> keys, string? destinationKey, CancellationToken ct);

    Task<Result<Unit>> DuplicateAsync(string accountKey, string key, CancellationToken ct);

    /// <summary>Moves to the trash in one dated batch (<see cref="FileTrash.BatchName"/>); the delete of something already in the trash, or in a shared account without a trash, is permanent and the caller asks first (B-FILE-011).</summary>
    Task<Result<Unit>> TrashAsync(string accountKey, IReadOnlyList<string> keys, CancellationToken ct);

    /// <summary>Back to the folder it came from (the batch's parent; the root when that is gone), names made unique; emptied batch folders are removed.</summary>
    Task<Result<Unit>> RestoreAsync(string accountKey, IReadOnlyList<string> keys, CancellationToken ct);

    /// <summary>Permanent delete (a folder with its subtree).</summary>
    Task<Result<Unit>> PurgeAsync(string accountKey, IReadOnlyList<string> keys, CancellationToken ct);

    Task<Result<Unit>> EmptyTrashAsync(string accountKey, CancellationToken ct);

    /// <summary>shareWith for one principal: write true or false, null removes the share (Rust files.rs:743).</summary>
    Task<Result<Unit>> ShareAsync(string accountKey, string key, string principalId, bool? write, CancellationToken ct);
}
