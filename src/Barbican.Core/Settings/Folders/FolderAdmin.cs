// Mirrors: see IFolderAdmin. Stub from the M6 plan section 4.8; owner srv-data replaces the class (constructor dependencies: IAccountManager, IMailService).

using Barbican.Core.Common;

namespace Barbican.Core.Settings.Folders;

/// <summary>The default <see cref="IFolderAdmin"/>.</summary>
public sealed class FolderAdmin : IFolderAdmin
{
    public event EventHandler<FolderAdminChangedEventArgs>? Changed
    {
        add { }
        remove { }
    }

    public Task<Result<string>> CreateAsync(string accountKey, string? parentId, string name, CancellationToken ct) => throw new PortPendingException("FolderAdmin.CreateAsync");

    public Task<Result<Unit>> RenameAsync(string accountKey, string id, string name, CancellationToken ct) => throw new PortPendingException("FolderAdmin.RenameAsync");

    public Task<Result<Unit>> MoveAsync(string accountKey, IReadOnlyList<string> ids, string? parentId, CancellationToken ct) => throw new PortPendingException("FolderAdmin.MoveAsync");

    public Task<Result<Unit>> DeleteAsync(string accountKey, string id, CancellationToken ct) => throw new PortPendingException("FolderAdmin.DeleteAsync");

    public Task<Result<Unit>> SubscribeAsync(string accountKey, string id, bool subscribed, CancellationToken ct) => throw new PortPendingException("FolderAdmin.SubscribeAsync");

    public Task<Result<Unit>> SetRoleAsync(string accountKey, string id, string? role, CancellationToken ct) => throw new PortPendingException("FolderAdmin.SetRoleAsync");

    public Task<Result<Unit>> MarkTreeReadAsync(string accountKey, string id, CancellationToken ct) => throw new PortPendingException("FolderAdmin.MarkTreeReadAsync");

    public Task<Result<FolderAdminPrefs>> PrefsAsync(string accountKey, CancellationToken ct) => throw new PortPendingException("FolderAdmin.PrefsAsync");

    public Task<Result<Unit>> SetPrefAsync(string accountKey, string folderId, FolderAdminPref pref, string? value, CancellationToken ct) => throw new PortPendingException("FolderAdmin.SetPrefAsync");
}
