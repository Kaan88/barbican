// Mirrors: Rust settings.rs:105 load (prefs.json, a missing or broken file reads as no values), :136 save (off the UI thread, LATEST serves
// reads until the write lands, the newest save wins, a superseded write is skipped), :122 flush, :185 put; Bulwark stores/settings-store.ts
// (the keys). PORT-PLAN.md §3.5: the file is read as a JsonObject, so keys this version does not know survive every save; M1-plan §6.3:
// in-memory snapshot, debounced asynchronous save, atomic write. spec B-SET-002, B-SET-004, B-SET-005.

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Barbican.Core.Storage;

/// <summary>prefs.json of a profile: one snapshot in memory, saved shortly after each change.</summary>
public sealed class PrefsStore : IPrefsStore, IPrefsEditor, IDisposable
{
    /// <summary>The file name (Rust settings.rs:35).</summary>
    public const string FileName = "prefs.json";

    /// <summary>How long a change waits for the next one before the file is written.</summary>
    public static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(300);

    private readonly string _path;
    private readonly TimeProvider _time;
    private readonly Lock _lock = new();
    private readonly SemaphoreSlim _write = new(1, 1);
    private readonly Task<JsonObject> _load;
    private PrefsSnapshot? _current;
    private ITimer? _timer;
    private long _changeSeq;
    private long _savedSeq;

    public PrefsStore(IProfilePaths paths, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(time);
        _path = Path.Combine(paths.ConfigDir, FileName);
        _time = time;

        // read off the calling thread (the composition runs on the UI thread); the first Current waits for it
        _load = Task.Run(() => Read(_path));
    }

    /// <inheritdoc/>
    public event EventHandler? Changed;

    /// <inheritdoc/>
    public PrefsSnapshot Current
    {
        get
        {
            lock (_lock)
            {
                if (_current is { } c)
                {
                    return c;
                }
            }

            // the start-up task (PrefsLoadTask) awaits the load before the first window, so this wait is over at once in the app; only a
            // read during composition or in a tool blocks, and briefly
#pragma warning disable VSTHRD002 // see above: the load has finished or is a single small file read on the thread pool
            var loaded = _load.GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
            lock (_lock)
            {
                return _current ??= PrefsSnapshot.FromJson(loaded);
            }
        }
    }

    /// <summary>Waits until prefs.json has been read (the start-up task calls it before the first window).</summary>
    public async Task WaitLoadedAsync(CancellationToken ct)
    {
#pragma warning disable VSTHRD003 // the load was started by this object's constructor on the thread pool; awaiting it cannot deadlock
        _ = await _load.WaitAsync(ct).ConfigureAwait(false);
#pragma warning restore VSTHRD003
    }

    /// <summary>The prefs file.</summary>
    public string FilePath => _path;

    /// <inheritdoc/>
    public void Put(string key, JsonNode? value)
    {
        ArgumentNullException.ThrowIfNull(key);
        _ = Current; // loaded
        lock (_lock)
        {
            var values = (JsonObject)_current!.Values.DeepClone();
            if (value is null)
            {
                values.Remove(key);
            }
            else
            {
                values[key] = value.Parent is null ? value : value.DeepClone();
            }

            _current = PrefsSnapshot.FromJson(values);
            _changeSeq++;
            _timer ??= _time.CreateTimer(_ => _ = SaveAsync(CancellationToken.None), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _timer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    public Task FlushAsync(CancellationToken ct)
    {
        lock (_lock)
        {
            _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        return SaveAsync(ct);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _timer?.Dispose();
            _timer = null;
        }

        _write.Dispose();
    }

    private static JsonObject Read(string path)
    {
        var text = AtomicFile.ReadAllTextOrNull(path);
        if (text is null)
        {
            return [];
        }

        try
        {
            return JsonNode.Parse(text) as JsonObject ?? [];
        }
        catch (JsonException)
        {
            // Rust reads a broken file as no values and overwrites it on the next save; keep one copy of what was there
            try
            {
                File.Copy(path, path + ".bad", overwrite: true);
            }
            catch (IOException)
            {
                // the copy is a courtesy
            }

            return [];
        }
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        await _write.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            string text;
            long seq;
            lock (_lock)
            {
                seq = _changeSeq;
                if (seq == _savedSeq || _current is null)
                {
                    return;
                }

                text = _current.Values.ToJsonString();
            }

            await AtomicFile.WriteAllTextAsync(_path, text, ct).ConfigureAwait(false);
            lock (_lock)
            {
                _savedSeq = Math.Max(_savedSeq, seq);
            }
        }
        finally
        {
            _write.Release();
        }
    }
}
