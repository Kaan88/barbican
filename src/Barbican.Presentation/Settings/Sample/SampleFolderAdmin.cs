// Mirrors: M6 plan section 3.5 (D-M6-00): an in-memory fake of IFolderAdmin that behaves like the real one where the M5 sample stores did not: its calls complete off the caller's thread, and its Changed is raised from a pool thread, so a view built on it meets the same threading the real service has (M5 found two such bugs, the real store loading on the UI thread and raising from the pool).
// Frozen in the M6 plan commit; owner integrator. Used by view-model tests and by sample-data runs (BARBICAN_SAMPLE=1); the data stream's real class never uses it.

using Barbican.Core.Common;
using Barbican.Core.Settings.Folders;

namespace Barbican.Presentation.Settings.Sample;

/// <summary>An in-memory <see cref="IFolderAdmin"/>; it keeps the preferences and raises Changed for every write (the folder list itself comes from the sidebar's mailbox source).</summary>
public sealed class SampleFolderAdmin : IFolderAdmin
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, string> _icons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _counts = new(StringComparer.Ordinal);
    private readonly HashSet<string> _pinned = new(StringComparer.Ordinal);
    private readonly HashSet<string> _muted = new(StringComparer.Ordinal);
    private int _next = 1;

    public event EventHandler<FolderAdminChangedEventArgs>? Changed;

    public async Task<Result<string>> CreateAsync(string accountKey, string? parentId, string name, CancellationToken ct)
    {
        var id = await DoneAsync(accountKey, () => "sf" + _next++).ConfigureAwait(false);
        return Result<string>.Ok(id);
    }

    public Task<Result<Unit>> RenameAsync(string accountKey, string id, string name, CancellationToken ct) => OkAsync(accountKey, () => { });

    public Task<Result<Unit>> MoveAsync(string accountKey, IReadOnlyList<string> ids, string? parentId, CancellationToken ct) => OkAsync(accountKey, () => { });

    public Task<Result<Unit>> DeleteAsync(string accountKey, string id, CancellationToken ct) => OkAsync(accountKey, () => { });

    public Task<Result<Unit>> SubscribeAsync(string accountKey, string id, bool subscribed, CancellationToken ct) => OkAsync(accountKey, () => { });

    public Task<Result<Unit>> SetRoleAsync(string accountKey, string id, string? role, CancellationToken ct) => OkAsync(accountKey, () => { });

    public Task<Result<Unit>> MarkTreeReadAsync(string accountKey, string id, CancellationToken ct) => OkAsync(accountKey, () => { });

    public Task<Result<FolderAdminPrefs>> PrefsAsync(string accountKey, CancellationToken ct) =>
        SampleThreading.OffThreadAsync(() =>
        {
            lock (_lock)
            {
                return Result<FolderAdminPrefs>.Ok(new FolderAdminPrefs(new Dictionary<string, string>(_icons, StringComparer.Ordinal), new Dictionary<string, string>(_counts, StringComparer.Ordinal), new HashSet<string>(_pinned, StringComparer.Ordinal), new HashSet<string>(_muted, StringComparer.Ordinal)));
            }
        });

    public Task<Result<Unit>> SetPrefAsync(string accountKey, string folderId, FolderAdminPref pref, string? value, CancellationToken ct) =>
        OkAsync(accountKey, () =>
        {
            lock (_lock)
            {
                var on = string.Equals(value, "true", StringComparison.Ordinal);
                switch (pref)
                {
                    case FolderAdminPref.Icon:
                        Put(_icons, folderId, value);
                        break;
                    case FolderAdminPref.CountMode:
                        Put(_counts, folderId, value);
                        break;
                    case FolderAdminPref.Pin:
                        _ = on ? _pinned.Add(folderId) : _pinned.Remove(folderId);
                        break;
                    default:
                        _ = on ? _muted.Add(folderId) : _muted.Remove(folderId);
                        break;
                }
            }
        });

    private static void Put(Dictionary<string, string> map, string key, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            _ = map.Remove(key);
        }
        else
        {
            map[key] = value;
        }
    }

    private async Task<T> DoneAsync<T>(string accountKey, Func<T> work)
    {
        var r = await SampleThreading.OffThreadAsync(work).ConfigureAwait(false);
        SampleThreading.RaiseOnPool(() => Changed?.Invoke(this, new FolderAdminChangedEventArgs(accountKey)));
        return r;
    }

    private async Task<Result<Unit>> OkAsync(string accountKey, Action work)
    {
        await DoneAsync(accountKey, () =>
        {
            work();
            return 0;
        }).ConfigureAwait(false);
        return Result<Unit>.Ok(default);
    }
}
