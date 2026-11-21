// Mirrors: Rust files.rs (load, ST, local() / prefs_list: favourites and recent files in files.json), reader.rs:887 folder_tree; Bulwark stores/files-store.ts, lib/files-sync/, hooks for favourites and recent (files-favorites, files-recent-files); PORT-PLAN section 3.4 (push type FileNode); G-044 (files live).
// Stalwart crates/jmap/src/file/{get,query}.rs. B-FILE-003..005, B-FILE-014. Contract from the M5 plan section 4.7; owner files-data.

using Barbican.Core.Common;

namespace Barbican.Core.Files;

/// <summary>The in-memory file nodes of every signed-in login, plus the device-local favourites and recent files.</summary>
public interface IFileStore
{
    event EventHandler<FileChangedEventArgs>? Changed;

    /// <summary>Loads every node of the login and its shared accounts (FileNode/query paged, FileNode/get in chunks of maxObjectsInGet; the parent filter is client side, J-075); empty without the filenode capability.</summary>
    Task<Result<FileSnapshot>> LoadAsync(string accountKey, CancellationToken ct);

    /// <summary>The login's server advertises the filenode capability: the Files app and the composer's "Attach from Files" are offered only then (Bulwark's supportsFiles).</summary>
    bool Supports(string accountKey);

    FileSnapshot? Snapshot(string accountKey);

    /// <summary>/changes for FileNode after a push state change; a full load on cannotCalculateChanges.</summary>
    Task<Result<Unit>> RefreshAsync(string accountKey, CancellationToken ct);

    /// <summary>Starred keys of this device (the profile's files.json); fires onFileFavoriteToggle.</summary>
    IReadOnlyList<string> Favourites { get; }

    void ToggleStar(string key);

    /// <summary>The most recently opened keys, newest first, at most 30 (Bulwark files-recent-files).</summary>
    IReadOnlyList<string> Recent { get; }

    void AddRecent(string key);

    void RemoveRecent(string key);
}
