// Mirrors: see IFileStore. Stub from the M5 plan section 4.7; owner files-data replaces the class (constructor dependencies: IAccountManager, IHookBus, IProfilePaths, TimeProvider).

using Barbican.Core.Common;

namespace Barbican.Core.Files;

/// <summary>The default <see cref="IFileStore"/>.</summary>
public sealed class FileStore : IFileStore
{
    public event EventHandler<FileChangedEventArgs>? Changed
    {
        add { }
        remove { }
    }

    public IReadOnlyList<string> Favourites => throw new PortPendingException("FileStore.Favourites");

    public IReadOnlyList<string> Recent => throw new PortPendingException("FileStore.Recent");

    public Task<Result<FileSnapshot>> LoadAsync(string accountKey, CancellationToken ct) => throw new PortPendingException("FileStore.LoadAsync");

    public FileSnapshot? Snapshot(string accountKey) => throw new PortPendingException("FileStore.Snapshot");

    public Task<Result<Unit>> RefreshAsync(string accountKey, CancellationToken ct) => throw new PortPendingException("FileStore.RefreshAsync");

    public void ToggleStar(string key) => throw new PortPendingException("FileStore.ToggleStar");

    public void AddRecent(string key) => throw new PortPendingException("FileStore.AddRecent");

    public void RemoveRecent(string key) => throw new PortPendingException("FileStore.RemoveRecent");
}
