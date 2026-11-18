// Mirrors: Bulwark components/settings/folder-settings.tsx (the tree, create, rename, delete, role, subscribe, icon, count mode, pin, mute, mark tree read, move under), Rust server_settings.rs folders; I-430. M6 plan section 4.8 (IFolderAdmin, folders from the sidebar's mailbox source).
// Deleting a folder asks first (D-7): the page asks, this model deletes. The row order is SrvUiFolderTreeRules. Owner srv-ui.

using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Settings.Folders;
using Barbican.Presentation.Common;

namespace Barbican.Presentation.Settings.Server;

/// <summary>The folders half of the Organize page.</summary>
public sealed class SrvUiFoldersViewModel : SrvUiPageModel
{
    private readonly IFolderAdmin _admin;
    private readonly ISrvUiFolderSource _source;
    private readonly HashSet<string> _collapsed = new(StringComparer.Ordinal);
    private FolderAdminPrefs _prefs = new(new Dictionary<string, string>(StringComparer.Ordinal), new Dictionary<string, string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal));
    private string? _selectedId;

    public SrvUiFoldersViewModel(IFolderAdmin admin, ISrvUiFolderSource source, IAccountManager? accounts = null, IDispatcher? dispatcher = null)
        : base(accounts, dispatcher)
    {
        ArgumentNullException.ThrowIfNull(admin);
        ArgumentNullException.ThrowIfNull(source);
        _admin = admin;
        _source = source;
        _admin.Changed += (_, e) =>
        {
            if (IsMine(e.AccountKey))
            {
                _ = ReloadQuietlyAsync();
            }
        };
        _source.Changed += (_, _) => Raise(nameof(Rows));
    }

    /// <summary>The tree in display order.</summary>
    public IReadOnlyList<SrvUiFolderRow> Rows => SrvUiFolderTreeRules.Flatten(_source.Nodes(), _collapsed);

    public string? SelectedId => _selectedId;

    public FolderAdminNode? Selected => _source.Nodes().FirstOrDefault(n => string.Equals(n.Id, _selectedId, StringComparison.Ordinal));

    public FolderAdminPrefs Prefs => _prefs;

    public int Unread(string id) => _source.Unread(id);

    public bool Subscribed(string id) => _source.Subscribed(id);

    public string IconOf(string id) => _prefs.Icons.TryGetValue(id, out var v) ? v : string.Empty;

    /// <summary>"unread" (default), "all" or "none".</summary>
    public string CountModeOf(string id) => _prefs.CountModes.TryGetValue(id, out var v) ? v : "unread";

    public void Select(string? id)
    {
        _selectedId = id;
        Raise(nameof(SelectedId));
    }

    public void ToggleCollapsed(string id)
    {
        if (!_collapsed.Remove(id))
        {
            _ = _collapsed.Add(id);
        }

        Raise(nameof(Rows));
    }

    public async Task LoadAsync(CancellationToken ct)
    {
        IsLoading = true;
        TakePrefs(await _admin.PrefsAsync(AccountKey, ct).ConfigureAwait(false));
        IsLoading = false;
    }

    /// <summary>Creates a folder (under the selected one when <paramref name="underSelected"/>).</summary>
    public async Task<bool> CreateAsync(string name, bool underSelected, CancellationToken ct)
    {
        var problem = NameProblem(name);
        if (problem is not null)
        {
            Error = problem;
            return false;
        }

        var result = await _admin.CreateAsync(AccountKey, underSelected ? _selectedId : null, name.Trim(), ct).ConfigureAwait(false);
        return Done(result);
    }

    public async Task<bool> RenameAsync(string name, CancellationToken ct)
    {
        var problem = NameProblem(name);
        if (_selectedId is null || problem is not null)
        {
            Error = problem;
            return false;
        }

        return Done(await _admin.RenameAsync(AccountKey, _selectedId, name.Trim(), ct).ConfigureAwait(false));
    }

    public async Task<bool> DeleteAsync(CancellationToken ct)
    {
        if (_selectedId is null)
        {
            return false;
        }

        var ok = Done(await _admin.DeleteAsync(AccountKey, _selectedId, ct).ConfigureAwait(false));
        if (ok)
        {
            Select(null);
        }

        return ok;
    }

    public async Task<bool> SetRoleAsync(string? role, CancellationToken ct) =>
        _selectedId is not null && Done(await _admin.SetRoleAsync(AccountKey, _selectedId, role, ct).ConfigureAwait(false));

    public async Task<bool> SubscribeAsync(bool subscribed, CancellationToken ct) =>
        _selectedId is not null && Done(await _admin.SubscribeAsync(AccountKey, _selectedId, subscribed, ct).ConfigureAwait(false));

    public async Task<bool> MarkTreeReadAsync(CancellationToken ct) =>
        _selectedId is not null && Done(await _admin.MarkTreeReadAsync(AccountKey, _selectedId, ct).ConfigureAwait(false));

    /// <summary>Moves the selected folder under another (null: the top); refused for itself, its own descendants and the role folders.</summary>
    public async Task<bool> MoveUnderAsync(string? parentId, CancellationToken ct)
    {
        if (_selectedId is null || !CanMoveUnder(_selectedId, parentId))
        {
            Error = "That folder cannot be moved there.";
            return false;
        }

        return Done(await _admin.MoveAsync(AccountKey, [_selectedId], parentId, ct).ConfigureAwait(false));
    }

    public async Task<bool> SetPrefAsync(FolderAdminPref pref, string? value, CancellationToken ct) =>
        _selectedId is not null && Done(await _admin.SetPrefAsync(AccountKey, _selectedId, pref, value, ct).ConfigureAwait(false));

    /// <summary>Folders the selected one may be moved under (the tree rules).</summary>
    public IReadOnlyList<FolderAdminNode> MoveTargets() => [.. _source.Nodes().Where(n => _selectedId is not null && CanMoveUnder(_selectedId, n.Id))];

    private static string? NameProblem(string name)
    {
        var n = name?.Trim() ?? string.Empty;
        if (n.Length == 0)
        {
            return "Enter a folder name.";
        }

        return n.Contains('/', StringComparison.Ordinal) ? "A folder name cannot contain a slash." : null;
    }

    private bool CanMoveUnder(string id, string? parentId)
    {
        var nodes = _source.Nodes();
        try
        {
            return FolderAdminRules.CanMoveUnder(nodes, id, parentId);
        }
        catch (PortPendingException)
        {
            var self = nodes.FirstOrDefault(n => string.Equals(n.Id, id, StringComparison.Ordinal));
            if (self is null || !string.IsNullOrEmpty(self.Role) || string.Equals(self.ParentId, parentId, StringComparison.Ordinal))
            {
                return false;
            }

            for (var at = parentId; at is not null; at = nodes.FirstOrDefault(n => string.Equals(n.Id, at, StringComparison.Ordinal))?.ParentId)
            {
                if (string.Equals(at, id, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }
    }

    private bool Done<T>(Result<T> result)
    {
        Error = result.IsOk ? null : result.Error!.Message;
        return result.IsOk;
    }

    private void TakePrefs(Result<FolderAdminPrefs> result)
    {
        if (!result.IsOk)
        {
            Error = result.Error!.Message;
            return;
        }

        _prefs = result.Value!;
        Raise(nameof(Prefs));
        Raise(nameof(Rows));
    }

    private async Task ReloadQuietlyAsync()
    {
        try
        {
            TakePrefs(await _admin.PrefsAsync(AccountKey, CancellationToken.None).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            // the page went away
        }
    }
}
