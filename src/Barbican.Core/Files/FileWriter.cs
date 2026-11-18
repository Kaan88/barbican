// Mirrors: see IFileWriter. Stub from the M5 plan section 4.8; owner files-data replaces the class (constructor dependencies: IAccountManager, IFileStore, IHookBus, TimeProvider).

using Barbican.Core.Common;

namespace Barbican.Core.Files;

/// <summary>The default <see cref="IFileWriter"/>.</summary>
public sealed class FileWriter : IFileWriter
{
    public Task<Result<string>> CreateFolderAsync(string accountKey, string? parentKey, string name, CancellationToken ct) => throw new PortPendingException("FileWriter.CreateFolderAsync");

    public Task<Result<Unit>> RenameAsync(string accountKey, string key, string newName, CancellationToken ct) => throw new PortPendingException("FileWriter.RenameAsync");

    public Task<Result<Unit>> MoveAsync(string accountKey, IReadOnlyList<string> keys, string? destinationKey, CancellationToken ct) => throw new PortPendingException("FileWriter.MoveAsync");

    public Task<Result<Unit>> CopyAsync(string accountKey, IReadOnlyList<string> keys, string? destinationKey, CancellationToken ct) => throw new PortPendingException("FileWriter.CopyAsync");

    public Task<Result<Unit>> DuplicateAsync(string accountKey, string key, CancellationToken ct) => throw new PortPendingException("FileWriter.DuplicateAsync");

    public Task<Result<Unit>> TrashAsync(string accountKey, IReadOnlyList<string> keys, CancellationToken ct) => throw new PortPendingException("FileWriter.TrashAsync");

    public Task<Result<Unit>> RestoreAsync(string accountKey, IReadOnlyList<string> keys, CancellationToken ct) => throw new PortPendingException("FileWriter.RestoreAsync");

    public Task<Result<Unit>> PurgeAsync(string accountKey, IReadOnlyList<string> keys, CancellationToken ct) => throw new PortPendingException("FileWriter.PurgeAsync");

    public Task<Result<Unit>> EmptyTrashAsync(string accountKey, CancellationToken ct) => throw new PortPendingException("FileWriter.EmptyTrashAsync");

    public Task<Result<Unit>> ShareAsync(string accountKey, string key, string principalId, bool? write, CancellationToken ct) => throw new PortPendingException("FileWriter.ShareAsync");
}
