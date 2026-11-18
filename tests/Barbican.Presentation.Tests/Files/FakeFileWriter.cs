// In-memory IFileWriter for the Files view model tests (the M5 plan names FilesFakes). Not a Rust row.

using Barbican.Core.Common;
using Barbican.Core.Files;

namespace Barbican.Presentation.Tests.Files;

internal sealed class FakeFileWriter : IFileWriter
{
    private static Task<Result<Unit>> DoneAsync() => Task.FromResult(Result<Unit>.Ok(default));

    public Task<Result<string>> CreateFolderAsync(string accountKey, string? parentKey, string name, CancellationToken ct) => Task.FromResult(Result<string>.Ok("new"));

    public Task<Result<Unit>> RenameAsync(string accountKey, string key, string newName, CancellationToken ct) => DoneAsync();

    public Task<Result<Unit>> MoveAsync(string accountKey, IReadOnlyList<string> keys, string? destinationKey, CancellationToken ct) => DoneAsync();

    public Task<Result<Unit>> CopyAsync(string accountKey, IReadOnlyList<string> keys, string? destinationKey, CancellationToken ct) => DoneAsync();

    public Task<Result<Unit>> DuplicateAsync(string accountKey, string key, CancellationToken ct) => DoneAsync();

    public Task<Result<Unit>> TrashAsync(string accountKey, IReadOnlyList<string> keys, CancellationToken ct) => DoneAsync();

    public Task<Result<Unit>> RestoreAsync(string accountKey, IReadOnlyList<string> keys, CancellationToken ct) => DoneAsync();

    public Task<Result<Unit>> PurgeAsync(string accountKey, IReadOnlyList<string> keys, CancellationToken ct) => DoneAsync();

    public Task<Result<Unit>> EmptyTrashAsync(string accountKey, CancellationToken ct) => DoneAsync();

    public Task<Result<Unit>> ShareAsync(string accountKey, string key, string principalId, bool? write, CancellationToken ct) => DoneAsync();
}
