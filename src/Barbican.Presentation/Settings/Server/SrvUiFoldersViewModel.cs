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
    private IReadOnlyList<SrvUiFolderSnapshot> _snapshot = [];
    private long _loadSequence;

    public SrvUiFoldersViewModel(IFolderAdmin admin, ISrvUiFolderSource source, IAccountManager? accounts = null, IDispatcher? dispatcher = null)
        : base(accounts, dispatcher)
    {
        ArgumentNullException.ThrowIfNull(admin);
        ArgumentNullException.ThrowIfNull(source);
        _admin = admin;
        _source = source;
        _admin.Changed += (_, e) =>
        {
            _ = ReloadQuietlyAsync(e.AccountKey);
        };
        _source.Changed += (_, _) => Raise(nameof(Rows));
    }

    /// <summary>The tree in display order.</summary>
    public IReadOnlyList<SrvUiFolderRow> Rows => IsLoaded(CaptureAccount()) ? SrvUiFolderTreeRules.Flatten(Nodes(), _collapsed) : [];

    public string? SelectedId => _selectedId;

    public FolderAdminNode? Selected => (IsLoaded(CaptureAccount()) ? Nodes() : []).FirstOrDefault(n => string.Equals(n.Id, _selectedId, StringComparison.Ordinal));

    public FolderAdminPrefs Prefs => _prefs;

    public int Unread(string id) => _snapshot.FirstOrDefault(x => string.Equals(x.Node.Id, id, StringComparison.Ordinal))?.Unread ?? 0;

    public bool Subscribed(string id) => _snapshot.FirstOrDefault(x => string.Equals(x.Node.Id, id, StringComparison.Ordinal))?.Subscribed ?? true;

    public string IconOf(string id) => _prefs.Icons.TryGetValue(id, out var v) ? v : string.Empty;

    /// <summary>"unread" (default), "all" or "none".</summary>
    public string CountModeOf(string id) => _prefs.CountModes.TryGetValue(id, out var v) ? v : "unread";

    public void Select(string? id)
    {
        if (id is not null && (!IsLoaded(CaptureAccount()) || !Nodes().Any(n => string.Equals(n.Id, id, StringComparison.Ordinal)))) return;
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
        var stamp = CaptureAccount();
        var sequence = Interlocked.Increment(ref _loadSequence);
        var result = await _admin.PrefsAsync(stamp.Key, ct).ConfigureAwait(false);
        var folders = await _source.LoadAsync(stamp.Key, ct).ConfigureAwait(false);
        await OnUiAsync(stamp, () =>
        {
            if (sequence != Interlocked.Read(ref _loadSequence)) return;
            TakePrefs(result);
            if (!folders.IsOk) Error = folders.Error!.Message;
            if (result.IsOk && folders.IsOk)
            {
                _snapshot = folders.Value!;
                if (_selectedId is not null && !Nodes().Any(n => string.Equals(n.Id, _selectedId, StringComparison.Ordinal))) _selectedId = null;
                MarkLoaded(stamp);
                Error = null;
                Raise(nameof(Rows));
                Raise(nameof(SelectedId));
            }

            IsLoading = false;
        }).ConfigureAwait(false);
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

        if (!RequireLoaded(out var stamp)) return false;
        var account = stamp.Key;
        var result = await _admin.CreateAsync(account, underSelected ? _selectedId : null, name.Trim(), ct).ConfigureAwait(false);
        return await DoneAsync(stamp, result).ConfigureAwait(false);
    }

    public async Task<bool> RenameAsync(string name, CancellationToken ct)
    {
        var problem = NameProblem(name);
        if (_selectedId is null || problem is not null)
        {
            Error = problem;
            return false;
        }

        if (!RequireLoaded(out var stamp)) return false;
        var account = stamp.Key;
        var result = await _admin.RenameAsync(account, _selectedId, name.Trim(), ct).ConfigureAwait(false);
        return await DoneAsync(stamp, result).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(CancellationToken ct)
    {
        if (_selectedId is null)
        {
            return false;
        }

        if (!RequireLoaded(out var stamp)) return false;
        var account = stamp.Key;
        var result = await _admin.DeleteAsync(account, _selectedId, ct).ConfigureAwait(false);
        var ok = false;
        await OnUiAsync(stamp, async () => { ok = Done(result); if (ok) { Select(null); await LoadAsync(ct).ConfigureAwait(true); } }).ConfigureAwait(false);
        return ok;
    }

    public async Task<bool> SetRoleAsync(string? role, CancellationToken ct)
    {
        if (_selectedId is null) { return false; }
        if (!RequireLoaded(out var stamp)) return false;
        var account = stamp.Key;
        var result = await _admin.SetRoleAsync(account, _selectedId, role, ct).ConfigureAwait(false);
        return await DoneAsync(stamp, result).ConfigureAwait(false);
    }

    public async Task<bool> SubscribeAsync(bool subscribed, CancellationToken ct)
    {
        if (_selectedId is null) { return false; }
        if (!RequireLoaded(out var stamp)) return false;
        var account = stamp.Key;
        var result = await _admin.SubscribeAsync(account, _selectedId, subscribed, ct).ConfigureAwait(false);
        return await DoneAsync(stamp, result).ConfigureAwait(false);
    }

    public async Task<bool> MarkTreeReadAsync(CancellationToken ct)
    {
        if (_selectedId is null) { return false; }
        if (!RequireLoaded(out var stamp)) return false;
        var account = stamp.Key;
        var result = await _admin.MarkTreeReadAsync(account, _selectedId, ct).ConfigureAwait(false);
        return await DoneAsync(stamp, result).ConfigureAwait(false);
    }

    /// <summary>Moves the selected folder under another (null: the top); refused for itself, its own descendants and the role folders.</summary>
    public async Task<bool> MoveUnderAsync(string? parentId, CancellationToken ct)
    {
        if (_selectedId is null || !CanMoveUnder(_selectedId, parentId))
        {
            Error = "That folder cannot be moved there.";
            return false;
        }

        if (!RequireLoaded(out var stamp)) return false;
        var account = stamp.Key;
        var result = await _admin.MoveAsync(account, [_selectedId], parentId, ct).ConfigureAwait(false);
        return await DoneAsync(stamp, result).ConfigureAwait(false);
    }

    public async Task<bool> SetPrefAsync(FolderAdminPref pref, string? value, CancellationToken ct)
    {
        if (_selectedId is null) { return false; }
        if (!RequireLoaded(out var stamp)) return false;
        var account = stamp.Key;
        var result = await _admin.SetPrefAsync(account, _selectedId, pref, value, ct).ConfigureAwait(false);
        return await DoneAsync(stamp, result).ConfigureAwait(false);
    }

    /// <summary>Folders the selected one may be moved under (the tree rules).</summary>
    public IReadOnlyList<FolderAdminNode> MoveTargets() => [.. Nodes().Where(n => _selectedId is not null && CanMoveUnder(_selectedId, n.Id))];

    private IReadOnlyList<FolderAdminNode> Nodes() => [.. _snapshot.Select(x => x.Node)];

    protected override void OnAccountSwitched()
    {
        _ = Interlocked.Increment(ref _loadSequence);
        _snapshot = [];
        _selectedId = null;
        _collapsed.Clear();
        _prefs = new(new Dictionary<string, string>(StringComparer.Ordinal), new Dictionary<string, string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal));
        Raise(nameof(Rows));
        Raise(nameof(SelectedId));
        Raise(nameof(Prefs));
        ReloadAfterSwitch(() => LoadAsync(CancellationToken.None));
    }

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
        var nodes = Nodes();
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

    private async Task<bool> DoneAsync<T>(AccountStamp stamp, Result<T> result)
    {
        var ok = false;
        await OnUiAsync(stamp, async () => { ok = Done(result); if (ok) await LoadAsync(CancellationToken.None).ConfigureAwait(true); }).ConfigureAwait(false);
        return ok;
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

    private async Task ReloadQuietlyAsync(string account)
    {
        try
        {
            var stamp = CaptureAccount();
            if (!string.Equals(account, stamp.Key, StringComparison.Ordinal)) return;
            var result = await _admin.PrefsAsync(account, CancellationToken.None).ConfigureAwait(false);
            await OnUiAsync(stamp, () => { if (IsLoaded(stamp) && !IsLoading) TakePrefs(result); }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // the page went away
        }
    }
}
