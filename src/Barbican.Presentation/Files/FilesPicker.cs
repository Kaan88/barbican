// The default IFilesPicker: queued answers first (automation), then the platform function the App head sets. Owner files-ui.

using System.Collections.Concurrent;

namespace Barbican.Presentation.Files;

/// <summary>Queued answers, then a platform function.</summary>
public sealed class FilesPicker : IFilesPicker
{
    private readonly ConcurrentQueue<IReadOnlyList<string>?> _files = new();
    private readonly ConcurrentQueue<string?> _folders = new();

    /// <summary>The platform function (the App head): true asks for a folder.</summary>
    public Func<bool, CancellationToken, Task<IReadOnlyList<string>?>>? Platform { get; set; }

    public void QueueFiles(IReadOnlyList<string>? paths) => _files.Enqueue(paths);

    public void QueueFolder(string? path) => _folders.Enqueue(path);

    public Task<IReadOnlyList<string>?> PickFilesAsync(CancellationToken ct) =>
        _files.TryDequeue(out var q) ? Task.FromResult(q) : Platform is null ? Task.FromResult<IReadOnlyList<string>?>(null) : Platform(false, ct);

    public async Task<string?> PickFolderAsync(CancellationToken ct)
    {
        if (_folders.TryDequeue(out var q))
        {
            return q;
        }

        var r = Platform is null ? null : await Platform(true, ct).ConfigureAwait(true);
        return r is { Count: > 0 } ? r[0] : null;
    }
}
