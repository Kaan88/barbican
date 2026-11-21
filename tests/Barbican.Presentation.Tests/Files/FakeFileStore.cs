// In-memory IFileStore for the Files view model tests (the M5 plan names FilesFakes). Not a Rust row.

using Barbican.Core.Common;
using Barbican.Core.Files;

namespace Barbican.Presentation.Tests.Files;

internal sealed class FakeFileStore(IReadOnlyList<FileEntry> entries) : IFileStore
{
    private readonly List<string> _fav = [];
    private readonly List<string> _recent = [];

    public event EventHandler<FileChangedEventArgs>? Changed;

    public IReadOnlyList<string> Favourites => _fav;

    public IReadOnlyList<string> Recent => _recent;

    public IReadOnlyList<FileEntry> Entries { get; set; } = entries;

    public Task<Result<FileSnapshot>> LoadAsync(string accountKey, CancellationToken ct) => Task.FromResult(Result<FileSnapshot>.Ok(Snapshot(accountKey)!));

    public bool Supports(string accountKey) => true;

    public FileSnapshot? Snapshot(string accountKey) => new(accountKey, Entries, null, "s");

    public Task<Result<Unit>> RefreshAsync(string accountKey, CancellationToken ct) => Task.FromResult(Result<Unit>.Ok(default));

    public void ToggleStar(string key)
    {
        if (!_fav.Remove(key))
        {
            _fav.Add(key);
        }
    }

    public void AddRecent(string key)
    {
        _ = _recent.Remove(key);
        _recent.Insert(0, key);
    }

    public void RemoveRecent(string key) => _ = _recent.Remove(key);

    public void Raise(string account) => Changed?.Invoke(this, new FileChangedEventArgs(account, []));
}
