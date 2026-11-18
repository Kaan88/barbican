// Mirrors: Rust files.rs (State :17, :204 open: nodes of every account with the filenode capability, :119 local() / prefs_list / set_prefs_list: favourites and recent in files.json, :147 is_starred / toggle_star, :160 remove_recent, :166 note_recent
// (Rust kept 10, the plan 30)), PORT-PLAN section 3.4 (push type FileNode), D-M5-09 (device-local favourites and recent); Bulwark stores/file-store.ts (favourites: files-favorites, recent: files-recent-files; onFileFavoriteToggle :1819).
// Stalwart crates/jmap/src/file/{get,query,changes}.rs (a state string per type; query cannot filter by parent in 0.16, J-075). B-FILE-003..005, B-FILE-014, G-044 (files live). Owner files-data.

using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;
using Barbican.Core.Storage;

namespace Barbican.Core.Files;

/// <summary>The default <see cref="IFileStore"/>: the nodes of every signed-in login in memory, the favourites and recent files of this device in the profile's files.json.</summary>
public sealed partial class FileStore : IFileStore, IDisposable
{
    /// <summary>How many recent keys are kept (D-M5-09; Rust kept 10).</summary>
    public const int RecentLimit = 30;

    /// <summary>The 13 properties asked of every node (Rust files.rs:36 PROPS).</summary>
    public static readonly IReadOnlyList<string> Props =
        ["id", "parentId", "name", "type", "blobId", "size", "created", "modified", "nodeType", "@type", "mediaType", "shareWith", "myRights"];

    private const string FavouritesKey = "filesFavorites";
    private const string RecentKey = "filesRecent";

    private readonly IAccountManager _accounts;
    private readonly IHookBus _hooks;
    private readonly string? _localPath;
    private readonly TimeProvider _time;
    private readonly bool _watchPush;
    private readonly Lock _lock = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, Held> _held = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CancellationTokenSource> _watchers = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _stop = new();
    private List<string>? _favourites;
    private List<string>? _recent;
    private JsonObject _localOther = [];
    private Task _saving = Task.CompletedTask;

    public FileStore(IAccountManager accounts, IHookBus hooks, IProfilePaths? paths = null, TimeProvider? time = null, bool watchPush = true)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(hooks);
        _accounts = accounts;
        _hooks = hooks;
        _localPath = paths is null ? null : Path.Combine(paths.ConfigDir, "files.json");
        _time = time ?? TimeProvider.System;
        _watchPush = watchPush;
    }

    /// <inheritdoc/>
    public event EventHandler<FileChangedEventArgs>? Changed;

    /// <inheritdoc/>
    public IReadOnlyList<string> Favourites
    {
        get
        {
            lock (_lock)
            {
                EnsureLocal();
                return [.. _favourites!];
            }
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> Recent
    {
        get
        {
            lock (_lock)
            {
                EnsureLocal();
                return [.. _recent!];
            }
        }
    }

    /// <inheritdoc/>
    public FileSnapshot? Snapshot(string accountKey)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        lock (_lock)
        {
            return _held.TryGetValue(accountKey, out var h) ? h.Snapshot : null;
        }
    }

    /// <inheritdoc/>
    public void ToggleStar(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        bool now;
        lock (_lock)
        {
            EnsureLocal();
            now = !_favourites!.Remove(key);
            if (now)
            {
                _favourites.Add(key);
            }

            SaveLocal();
        }

        var (account, entry) = Find(key);
        _hooks.Fire(HookNames.OnFileFavoriteToggle, () => new FileFavouritePayload(ViewOf(entry, key), now));
        Raise(new FileChangedEventArgs(account, [key]));
    }

    /// <inheritdoc/>
    public void AddRecent(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        lock (_lock)
        {
            EnsureLocal();
            _ = _recent!.Remove(key);
            _recent.Insert(0, key);
            if (_recent.Count > RecentLimit)
            {
                _recent.RemoveRange(RecentLimit, _recent.Count - RecentLimit);
            }

            SaveLocal();
        }
    }

    /// <inheritdoc/>
    public void RemoveRecent(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        lock (_lock)
        {
            EnsureLocal();
            if (_recent!.Remove(key))
            {
                SaveLocal();
            }
        }
    }

    /// <summary>Waits for the files.json writes (tests, shutdown).</summary>
    public async Task DrainAsync()
    {
        Task saving;
        lock (_lock)
        {
            saving = _saving;
        }

        await saving.ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _stop.Cancel();
        lock (_lock)
        {
            foreach (var w in _watchers.Values)
            {
                w.Cancel();
            }

            _watchers.Clear();
        }

        _stop.Dispose();
    }

    private void Raise(FileChangedEventArgs e) => Changed?.Invoke(this, e);

    private (string Account, FileEntry? Entry) Find(string key)
    {
        lock (_lock)
        {
            foreach (var (account, h) in _held)
            {
                if (h.Entries.Find(e => string.Equals(e.Key, key, StringComparison.Ordinal)) is { } entry)
                {
                    return (account, entry);
                }
            }
        }

        return (string.Empty, null);
    }

    private string PathOf(string? parentKey)
    {
        lock (_lock)
        {
            foreach (var h in _held.Values)
            {
                if (parentKey is null || h.Entries.Exists(e => string.Equals(e.Key, parentKey, StringComparison.Ordinal)))
                {
                    return FilePaths.Of(h.Entries, parentKey);
                }
            }
        }

        return string.Empty;
    }

    private FileResourceView ViewOf(FileEntry? entry, string key) =>
        entry is null ? new FileResourceView(key, string.Empty, "file", 0, string.Empty, string.Empty, null) : FileResourceView.From(entry, PathOf(entry.ParentKey));

    private void PreloadLocal()
    {
        lock (_lock)
        {
            EnsureLocal();
        }
    }

    // the local lists are read once; the caller holds _lock
    private void EnsureLocal()
    {
        if (_favourites is not null)
        {
            return;
        }

        _favourites = [];
        _recent = [];
        if (_localPath is null || AtomicFile.ReadAllTextOrNull(_localPath) is not { } text)
        {
            return;
        }

        try
        {
            if (JsonNode.Parse(text) is JsonObject o)
            {
                _favourites = [.. (o[FavouritesKey] as JsonArray ?? []).Select(AsString).OfType<string>()];
                _recent = [.. (o[RecentKey] as JsonArray ?? []).Select(AsString).OfType<string>().Take(RecentLimit)];
                _ = o.Remove(FavouritesKey);
                _ = o.Remove(RecentKey);
                _localOther = o;
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // a damaged files.json starts empty
        }
    }

    private static string? AsString(JsonNode? x) => x is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    // the caller holds _lock; the write goes to the thread pool, in order, atomically
    private void SaveLocal()
    {
        if (_localPath is not { } path)
        {
            return;
        }

        var o = (JsonObject)_localOther.DeepClone();
        o[FavouritesKey] = new JsonArray([.. _favourites!.Select(k => (JsonNode)k)]);
        o[RecentKey] = new JsonArray([.. _recent!.Select(k => (JsonNode)k)]);
        var text = o.ToJsonString();
        _saving = _saving.ContinueWith(_ => WriteQuietly(path, text), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

#pragma warning disable CA1031 // device-local lists: a failed write must not break the app (the next change writes again)
    private static void WriteQuietly(string path, string text)
    {
        try
        {
            AtomicFile.WriteAllText(path, text);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // nothing to tell the caller
        }
    }
#pragma warning restore CA1031

    private sealed class Held(string primary)
    {
        public string Primary { get; } = primary;

        public List<FileEntry> Entries { get; set; } = [];

        public FileNameRules? Rules { get; set; }

        public bool Draft18 { get; set; }

        /// <summary>The state string per JMAP account.</summary>
        public Dictionary<string, string?> States { get; } = new(StringComparer.Ordinal);

        public FileSnapshot Snapshot { get; private set; } = new(string.Empty, [], null, null);

        public void Build(string accountKey) => Snapshot = new FileSnapshot(accountKey, [.. Entries], Rules, States.GetValueOrDefault(Primary), Draft18);
    }
}
