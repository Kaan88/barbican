// Mirrors: see IFilesViewModel. Rust files.rs (ST, render, open, activate), Bulwark components/files/files-app.tsx, file-browser.tsx, files-sidebar.tsx, lib/files-settings.ts; B-FILE-012, B-FILE-014, G-015. Owner files-ui.

using System.ComponentModel;
using System.Runtime.CompilerServices;
using Barbican.Core.Files;
using Barbican.Core.Plugins.Hooks;

namespace Barbican.Presentation.Files;

/// <summary>The default <see cref="IFilesViewModel"/>: the list of one place of one login, with its sort, search and selection.</summary>
public sealed class FilesViewModel : IFilesViewModel
{
    public const string FavouritesLocation = "*favourites";
    public const string RecentLocation = "*recent";
    public const string SharedLocation = "*shared";

    private readonly IFileStore _store;
    private readonly IHookBus? _hooks;
    private string? _location;
    private IReadOnlyList<FileEntry> _entries = [];
    private HashSet<string> _selected = [];
    private string? _anchor;
    private FilesSortKey _sortKey;
    private bool _descending;
    private FilesViewMode _viewMode;
    private string _search = string.Empty;
    private bool _showHidden;
    private bool _loading;
    private string? _error;
    private string _accountKey = string.Empty;

    public FilesViewModel(IFileStore store, IFileWriter writer, IHookBus? hooks = null, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        Writer = writer;
        _hooks = hooks;
        Time = time ?? TimeProvider.System;
        _store.Changed += (_, e) =>
        {
            if (string.Equals(e.AccountKey, _accountKey, StringComparison.Ordinal))
            {
                Rebuild();
            }
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IFileWriter Writer { get; }

    public TimeProvider Time { get; }

    /// <summary>The login whose files are shown (set by the screen from the account scope picker, I-066).</summary>
    public string AccountKey
    {
        get => _accountKey;
        set
        {
            if (Set(ref _accountKey, value))
            {
                _location = null;
                _selected = [];
                Rebuild();
            }
        }
    }

    public string? Location => _location;

    public IReadOnlyList<FileEntry> Entries => _entries;

    public IReadOnlySet<string> SelectedKeys => _selected;

    public FilesSortKey SortKey
    {
        get => _sortKey;
        set
        {
            if (Set(ref _sortKey, value))
            {
                Rebuild();
            }
        }
    }

    public bool SortDescending
    {
        get => _descending;
        set
        {
            if (Set(ref _descending, value))
            {
                Rebuild();
            }
        }
    }

    public FilesViewMode ViewMode
    {
        get => _viewMode;
        set => Set(ref _viewMode, value);
    }

    public bool ShowHidden
    {
        get => _showHidden;
        set
        {
            if (Set(ref _showHidden, value))
            {
                Rebuild();
            }
        }
    }

    public string SearchText
    {
        get => _search;
        set
        {
            if (Set(ref _search, value ?? string.Empty))
            {
                Rebuild();
            }
        }
    }

    public bool IsLoading => _loading;

    public string? LoadError => _error;

    /// <summary>The folder chain from the root to the current folder (empty at a place that is not a folder).</summary>
    public IReadOnlyList<FileEntry> Breadcrumb
    {
        get
        {
            var all = _store.Snapshot(_accountKey)?.Entries ?? [];
            var byKey = all.ToDictionary(e => e.Key, StringComparer.Ordinal);
            var chain = new List<FileEntry>();
            var k = _location;
            while (k is not null && !k.StartsWith('*') && byKey.TryGetValue(k, out var e) && chain.Count < 64)
            {
                chain.Insert(0, e);
                k = e.ParentKey;
            }

            return chain;
        }
    }

    /// <summary>True when the place is the trash (the list then offers Restore and Delete forever).</summary>
    public bool InTrash => string.Equals(_location, FileTrash.TrashLocation, StringComparison.Ordinal);

    /// <summary>The sub-folders of a folder (null: the root) for the sidebar tree, trash excluded, natural name order.</summary>
    public IReadOnlyList<FileEntry> Folders(string? parentKey)
    {
        var all = _store.Snapshot(_accountKey)?.Entries ?? [];
        var kids = all.Where(e => e.IsDirectory && string.Equals(e.ParentKey, parentKey, StringComparison.Ordinal)).ToList();
        return FilesListRules.Sort(FilesListRules.Visible(kids, _showHidden), FilesSortKey.Name, false);
    }

    public void Navigate(string? location)
    {
        if (string.Equals(_location, location, StringComparison.Ordinal))
        {
            return;
        }

        _location = location;
        _selected = [];
        _anchor = null;
        _search = string.Empty;
        Rebuild();
        Raise(nameof(Location));
        Raise(nameof(Breadcrumb));
        Raise(nameof(InTrash));
        Raise(nameof(SearchText));
        Raise(nameof(SelectedKeys));
        Fire(HookNames.OnFileNavigate, new FileNavigatePayload(location ?? string.Empty));
    }

    /// <summary>Click selects one; extend (Ctrl) toggles one in the set.</summary>
    public void SelectFile(string key, bool extend)
    {
        if (extend)
        {
            if (!_selected.Remove(key))
            {
                _ = _selected.Add(key);
            }
        }
        else
        {
            _selected = [key];
        }

        _anchor = key;
        SelectionChanged();
    }

    /// <summary>Shift-click: everything between the anchor and <paramref name="key"/> in the visible order.</summary>
    public void SelectRange(string key)
    {
        var keys = _entries.Select(e => e.Key).ToList();
        var a = _anchor is null ? -1 : keys.IndexOf(_anchor);
        var b = keys.IndexOf(key);
        if (a < 0 || b < 0)
        {
            SelectFile(key, false);
            return;
        }

        _selected = [.. keys.Skip(Math.Min(a, b)).Take(Math.Abs(a - b) + 1)];
        SelectionChanged();
    }

    public void SelectAll()
    {
        _selected = [.. _entries.Select(e => e.Key)];
        SelectionChanged();
    }

    public void ClearSelection()
    {
        if (_selected.Count > 0)
        {
            _selected = [];
            SelectionChanged();
        }
    }

    /// <summary>Replaces the selection (the marquee).</summary>
    public void SelectMany(IEnumerable<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        _selected = [.. keys];
        SelectionChanged();
    }

    public async Task RefreshAsync(CancellationToken ct)
    {
        _loading = true;
        Raise(nameof(IsLoading));
        try
        {
            var r = await _store.RefreshAsync(_accountKey, ct).ConfigureAwait(true);
            if (!r.IsOk)
            {
                var load = await _store.LoadAsync(_accountKey, ct).ConfigureAwait(true);
                _error = load.IsOk ? null : load.Error?.ToString();
            }
            else
            {
                _error = null;
            }
        }
        finally
        {
            _loading = false;
            Raise(nameof(IsLoading));
            Raise(nameof(LoadError));
            Rebuild();
        }
    }

    private void Rebuild()
    {
        var all = _store.Snapshot(_accountKey)?.Entries ?? [];
        IReadOnlyList<FileEntry> source = _location switch
        {
            null => [.. all.Where(e => e.ParentKey is null && !e.Shared)],
            FavouritesLocation => ByKeys(all, _store.Favourites),
            RecentLocation => ByKeys(all, _store.Recent),
            SharedLocation => [.. all.Where(e => e.Shared && e.ParentKey is null)],
            FileTrash.TrashLocation => [.. FileTrash.Items(all).Select(t => t.Entry)],
            var folder => [.. all.Where(e => string.Equals(e.ParentKey, folder, StringComparison.Ordinal))],
        };
        var visible = FilesListRules.Visible(source, _showHidden);
        if (_search.Length > 0)
        {
            visible = [.. visible.Where(e => e.Name.Contains(_search, StringComparison.OrdinalIgnoreCase))];
        }

        // Recent keeps its own order (newest first); everything else is sorted.
        _entries = string.Equals(_location, RecentLocation, StringComparison.Ordinal) ? visible : FilesListRules.Sort(visible, _sortKey, _descending);
        var present = _entries.Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
        if (_selected.RemoveWhere(k => !present.Contains(k)) > 0)
        {
            SelectionChanged();
        }

        Raise(nameof(Entries));
    }

    private static List<FileEntry> ByKeys(IReadOnlyList<FileEntry> all, IReadOnlyList<string> keys)
    {
        var byKey = all.ToDictionary(e => e.Key, StringComparer.Ordinal);
        return [.. keys.Where(byKey.ContainsKey).Select(k => byKey[k])];
    }

    private void SelectionChanged()
    {
        Raise(nameof(SelectedKeys));
        Fire(HookNames.OnFileSelectionChange, new FileSelectionPayload(_selected.Count));
    }

    private void Fire<T>(string hook, T payload)
    {
        if (_hooks is { } h && h.HasHandlers(hook))
        {
            _ = h.NotifyAsync(hook, payload, CancellationToken.None).AsTask();
        }
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        Raise(name);
        return true;
    }

    private void Raise(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
