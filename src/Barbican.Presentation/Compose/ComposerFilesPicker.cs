// Mirrors: Bulwark components/files/file-picker-dialog.tsx:31-135 (isFolder is "no blob", folders first by name, the shared roots of others behind "Shared with me",
// MAX_SEARCH_RESULTS 200 over the whole tree with the folder path under each hit, hidden names unless the Files setting says otherwise), :46-316 (lists the whole tree once
// and browses it locally; multi-select; the picker never touches the Files app's own place and selection);
// components/email/email-composer.tsx:3182-3225 and :6467-6475, :6735-6741 (the composer's "Attach from Files" opens it for the composer's account).
// Stalwart's FileNode (draft-ietf-jmap-filenode) has folders without a blob and files with one: those blobs are what the composer attaches. Owner composer-ui.

using Barbican.Core.Common;
using Barbican.Core.Files;
using Barbican.Presentation.Files;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Barbican.Presentation.Compose;

/// <summary>The attach-from-Files picker: the whole node list of one login, browsed locally, with a search over every file and a multi-selection.</summary>
public sealed class ComposerFilesPicker : ObservableObject
{
    /// <summary>The level that holds what other people share with the login (Bulwark's "#shared": JMAP ids are base64url, so "#" never collides).</summary>
    public const string SharedLocation = "#shared";

    /// <summary>As many search hits as Bulwark shows (file-picker-dialog.tsx:21), the rest is narrowed by typing more.</summary>
    public const int MaxSearchResults = 200;

    private readonly IFileStore _store;
    private readonly bool _showHidden;
    private List<ComposerFilesCrumb> _trail = [];
    private readonly List<FileEntry> _selected = [];
    private IReadOnlyList<FileEntry> _all = [];
    private IReadOnlyList<ComposerFilesRow> _rows = [];
    private string _search = string.Empty;
    private bool _loading;
    private bool _failed;

    public ComposerFilesPicker(IFileStore store, string accountKey, bool showHiddenFiles = false)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(accountKey);
        _store = store;
        _showHidden = showHiddenFiles;
        AccountKey = accountKey;
    }

    /// <summary>The login whose files are listed.</summary>
    public string AccountKey { get; }

    /// <summary>The listing loaded (or is loading, or failed: <see cref="LoadFailed"/>).</summary>
    public bool IsLoading
    {
        get => _loading;
        private set => SetProperty(ref _loading, value);
    }

    /// <summary>The tree could not be read: the card offers Retry (Bulwark "Your files could not be loaded").</summary>
    public bool LoadFailed
    {
        get => _failed;
        private set => SetProperty(ref _failed, value);
    }

    /// <summary>The rows of the current level, or of the search (folders first, then names; folders only open, files tick).</summary>
    public IReadOnlyList<ComposerFilesRow> Rows
    {
        get => _rows;
        private set
        {
            if (SetProperty(ref _rows, value))
            {
                Raise(nameof(IsEmpty));
                Raise(nameof(EmptyText));
            }
        }
    }

    /// <summary>The folders opened below "My files" (empty at the root).</summary>
    public IReadOnlyList<ComposerFilesCrumb> Trail
    {
        get => _trail;
        private set
        {
            _trail = [.. value];
            OnPropertyChanged();
            Raise(nameof(AtRoot));
            Raise(nameof(EmptyText));
        }
    }

    /// <summary>What the search box asks for (trimmed for matching).</summary>
    public string SearchText
    {
        get => _search;
        set
        {
            if (SetProperty(ref _search, value ?? string.Empty))
            {
                Rebuild();
                Raise(nameof(Searching));
            }
        }
    }

    /// <summary>A search is on: the rows are hits from the whole tree, not a level.</summary>
    public bool Searching => EffectiveQuery.Length > 0;

    /// <summary>"My files" is the current level.</summary>
    public bool AtRoot => _trail.Count == 0 && !Searching;

    /// <summary>Nothing on screen to click (not while loading or after a failure).</summary>
    public bool IsEmpty => Rows.Count == 0;

    /// <summary>The line in the empty list: a search with no hits, a login with no files yet, or an empty folder (Bulwark types them apart).</summary>
    public string EmptyText => Searching ? "No files match your search" : AtRoot ? "No files yet" : "This folder is empty";

    /// <summary>The picked files, in the order they were ticked (never a folder).</summary>
    public IReadOnlyList<FileEntry> Picked => [.. _selected];

    public bool CanAttach => _selected.Count > 0;

    public int SelectedCount => _selected.Count;

    /// <summary>"Attach", "Attach 1 file", "Attach 3 files" (files_picker.attach).</summary>
    public string AttachLabel => _selected.Count switch
    {
        0 => "Attach",
        1 => "Attach 1 file",
        var n => $"Attach {n} files",
    };

    private string EffectiveQuery => _search.Trim();

    /// <summary>Reads the login's nodes (the store's snapshot when it has one) and lists the root; a failure leaves <see cref="LoadFailed"/> set for Retry.</summary>
    public async Task LoadAsync(CancellationToken ct)
    {
        IsLoading = true;
        LoadFailed = false;
        try
        {
            var snapshot = _store.Snapshot(AccountKey);
            if (snapshot is null)
            {
                var loaded = await _store.LoadAsync(AccountKey, ct).ConfigureAwait(true);
                if (!loaded.IsOk)
                {
                    LoadFailed = true;
                    return;
                }

                snapshot = loaded.Value!;
            }

            _all = snapshot.Entries;
        }
        catch (Exception e) when (e is HttpRequestException or IOException or PortPendingException or UnauthorizedAccessException)
        {
            LoadFailed = true;
            return;
        }
        finally
        {
            IsLoading = false;
        }

        Rebuild();
    }

    /// <summary>Back to "My files".</summary>
    public void OpenRoot()
    {
        if (_trail.Count == 0 && EffectiveQuery.Length == 0)
        {
            return;
        }

        Trail = [];
        _search = string.Empty;
        Raise(nameof(SearchText));
        Raise(nameof(Searching));
        Rebuild();
    }

    /// <summary>Opens a folder row (or "Shared with me"); the search is cleared, as Bulwark does.</summary>
    public void OpenFolder(string key, string name)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(name);
        _trail.Add(new ComposerFilesCrumb(key, name));
        Trail = [.. _trail];
        _search = string.Empty;
        Raise(nameof(SearchText));
        Raise(nameof(Searching));
        Rebuild();
    }

    /// <summary>Opens the crumb at <paramref name="index"/> of <see cref="Trail"/> and drops the ones after it.</summary>
    public void GoToCrumb(int index)
    {
        if (index < 0 || index >= _trail.Count)
        {
            return;
        }

        _trail.RemoveRange(index + 1, _trail.Count - index - 1);
        Trail = [.. _trail];
        Rebuild();
    }

    /// <summary>Ticks or unticks a file row (folders are never part of the selection).</summary>
    public void Select(string key, bool selected)
    {
        ArgumentNullException.ThrowIfNull(key);
        _ = _selected.RemoveAll(e => string.Equals(e.Key, key, StringComparison.Ordinal));
        if (selected && _all.FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.Ordinal)) is { IsDirectory: false } file)
        {
            _selected.Add(file);
        }

        Raise(nameof(Picked));
        Raise(nameof(CanAttach));
        Raise(nameof(SelectedCount));
        Raise(nameof(AttachLabel));
    }

    public bool IsSelected(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _selected.Exists(e => string.Equals(e.Key, key, StringComparison.Ordinal));
    }

    private void Rebuild()
    {
        var byKey = _all.GroupBy(e => e.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var query = EffectiveQuery;
        List<ComposerFilesRow> rows;
        if (query.Length > 0)
        {
            // the search looks at files of the whole tree, hidden names unless the Files setting allows them (file-picker-dialog.tsx:96-102)
            var hits = _all.Where(e => !e.IsDirectory && e.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
            rows = [.. FilesListRules.Sort([.. hits], FilesSortKey.Name, false)
                .Where(e => _showHidden || !e.Name.StartsWith('.'))
                .Take(MaxSearchResults)
                .Select(e => new ComposerFilesRow(e.Key, e.Name, false, e.Size, FolderPath(e, byKey)))];
        }
        else
        {
            var here = _trail.Count == 0 ? null : _trail[^1].Key;
            IReadOnlyList<FileEntry> level = here switch
            {
                SharedLocation => SharedRoots(byKey),
                null => [.. _all.Where(e => e.ParentKey is null && !e.Shared)],
                _ => [.. _all.Where(e => string.Equals(e.ParentKey, here, StringComparison.Ordinal))],
            };
            var visible = FilesListRules.Visible(level, _showHidden);
            rows = [.. FilesListRules.Sort(visible, FilesSortKey.Name, false)
                .Select(e => new ComposerFilesRow(e.Key, e.Name, e.IsDirectory, e.Size, null))];
            if (here is null && SharedRoots(byKey).Count > 0)
            {
                rows.Insert(0, new ComposerFilesRow(SharedLocation, "Shared with me", IsFolder: true, Size: 0, Detail: null));
            }
        }

        Rows = rows;
    }

    // what someone shares whose parent the login cannot see: the "Shared with me" level (file-picker-dialog.tsx:86-89)
    private List<FileEntry> SharedRoots(Dictionary<string, FileEntry> byKey) => [.. _all.Where(e => e.Shared && (e.ParentKey is null || !byKey.ContainsKey(e.ParentKey)))];

    // where a search hit lives, "Projects / Plans" (file-picker-dialog.tsx:110-121)
    private static string? FolderPath(FileEntry entry, Dictionary<string, FileEntry> byKey)
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var parent = entry.ParentKey;
        for (var i = 0; parent is not null && i < 64 && seen.Add(parent); i++)
        {
            if (!byKey.TryGetValue(parent, out var folder))
            {
                break;
            }

            names.Insert(0, folder.Name);
            parent = folder.ParentKey;
        }

        return names.Count == 0 ? null : string.Join(" / ", names);
    }

    private void Raise(string name) => OnPropertyChanged(name);
}
