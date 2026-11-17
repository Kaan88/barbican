// Mirrors: Rust search_history.rs:15 load, :20 save, :40 add, :46 remove (search-history.json, a JSON array of strings, ten newest first); Bulwark stores/search-history-store.ts.
// Kept locally under the profile (D-3). The list lives in memory (read once, off the UI thread when a path is given); each change writes the small file on a background task, in order,
// so the box never waits for the disk. A missing or damaged file is an empty history.

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Barbican.Core.Search;

/// <summary>The recent searches, kept in search-history.json.</summary>
public sealed class SearchHistoryStore
{
    /// <summary>The file name under the profile's config folder.</summary>
    public const string FileName = "search-history.json";

    private readonly string? _path;
    private readonly Lock _lock = new();
    private List<string>? _cache;
    private Task _writes = Task.CompletedTask;

    /// <summary>History in <c>search-history.json</c> under <paramref name="configDir"/>; null keeps it in memory (tests).</summary>
    public SearchHistoryStore(string? configDir)
    {
        _path = configDir is null ? null : Path.Combine(configDir, FileName);
        if (_path is not null)
        {
            _ = Task.Run(Load);
        }
    }

    /// <summary>Waits until every change so far is on disk (tests, shutdown).</summary>
    public Task FlushAsync()
    {
        Task pending;
        lock (_lock)
        {
            pending = _writes;
        }

        return pending;
    }

    /// <summary>The recent searches, newest first.</summary>
    public IReadOnlyList<string> Load()
    {
        lock (_lock)
        {
            return [.. LoadLocked()];
        }
    }

    /// <summary>Adds a query at the front (blank adds nothing).</summary>
    public void Add(string query)
    {
        lock (_lock)
        {
            var cur = LoadLocked();
            var next = SearchHistory.WithAdded(cur, query);
            if (!next.SequenceEqual(cur, StringComparer.Ordinal))
            {
                _cache = [.. next];
                Save();
            }
        }
    }

    /// <summary>Takes one entry out.</summary>
    public void Remove(string query)
    {
        lock (_lock)
        {
            var cur = LoadLocked();
            if (cur.RemoveAll(x => string.Equals(x, query, StringComparison.Ordinal)) > 0)
            {
                Save();
            }
        }
    }

    private List<string> LoadLocked()
    {
        if (_cache is not null)
        {
            return _cache;
        }

        _cache = [];
        if (_path is not null && File.Exists(_path))
        {
            try
            {
                _cache = [.. (JsonNode.Parse(File.ReadAllText(_path)) as JsonArray ?? []).Select(x => x is JsonValue v && v.TryGetValue<string>(out var s) ? s : null).OfType<string>()];
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
            {
                _cache = [];
            }
        }

        return _cache;
    }

    private void Save()
    {
        if (_path is null)
        {
            return;
        }

        var snapshot = new JsonArray([.. _cache!.Select(x => (JsonNode?)JsonValue.Create(x))]).ToJsonString();
        var path = _path;
        _writes = _writes.ContinueWith(_ => Write(path, snapshot), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    private static void Write(string path, string json)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // history is a convenience: a full disk or a locked file loses one entry, never a search
        }
    }
}
