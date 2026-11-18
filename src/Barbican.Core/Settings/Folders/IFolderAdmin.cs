// Mirrors: see FolderAdminNode. Hooks: none of the M6 hooks is a folder hook (the M2 sidebar hooks onMailboxChanged and friends keep firing from the sync layer). THREADING (D-M6-00): every call completes off the UI thread; Changed is raised on a pool thread (the Mailbox push delta that follows a write, or a write that finished).
// Contract from the M6 plan section 4.8; owner srv-data.

using Barbican.Core.Common;

namespace Barbican.Core.Settings.Folders;

/// <summary>Create, rename, move, delete, subscribe, role, pin, mute, icon, count mode and mark-read of folders.</summary>
public interface IFolderAdmin
{
    /// <summary>Creates a folder under <paramref name="parentId"/> (null: the top), subscribed; returns its id.</summary>
    Task<Result<string>> CreateAsync(string accountKey, string? parentId, string name, CancellationToken ct);

    Task<Result<Unit>> RenameAsync(string accountKey, string id, string name, CancellationToken ct);

    /// <summary>Moves folders under a parent (the bulk move of the page and the drag, I-430).</summary>
    Task<Result<Unit>> MoveAsync(string accountKey, IReadOnlyList<string> ids, string? parentId, CancellationToken ct);

    /// <summary>Deletes a folder and its messages (onDestroyRemoveEmails); the UI asks first (D-7).</summary>
    Task<Result<Unit>> DeleteAsync(string accountKey, string id, CancellationToken ct);

    Task<Result<Unit>> SubscribeAsync(string accountKey, string id, bool subscribed, CancellationToken ct);

    /// <summary>Gives the folder a role (null clears it); the previous holder loses it in the same Mailbox/set.</summary>
    Task<Result<Unit>> SetRoleAsync(string accountKey, string id, string? role, CancellationToken ct);

    /// <summary>Marks every message of the folder and its subfolders read (G-040).</summary>
    Task<Result<Unit>> MarkTreeReadAsync(string accountKey, string id, CancellationToken ct);

    Task<Result<FolderAdminPrefs>> PrefsAsync(string accountKey, CancellationToken ct);

    /// <summary>Sets one per-folder preference in the Inbox metadata with ifInState (retried on stateMismatch, as the template store); a null or empty value clears it; for Pin and Mute the value is "true" or "false".</summary>
    Task<Result<Unit>> SetPrefAsync(string accountKey, string folderId, FolderAdminPref pref, string? value, CancellationToken ct);

    /// <summary>Raised when folders or their preferences changed, with the account key, on a pool thread.</summary>
    event EventHandler<FolderAdminChangedEventArgs>? Changed;
}
