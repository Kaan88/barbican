// Mirrors: see IFileTransfer. Stub from the M5 plan section 4.9; owner files-data replaces the class (constructor dependencies: IAccountManager, IFileStore, IFileWriter, IHookBus, ISettingsStore).

using Barbican.Core.Common;

namespace Barbican.Core.Files;

/// <summary>The default <see cref="IFileTransfer"/>.</summary>
public sealed class FileTransfer : IFileTransfer
{
    public Task<Result<IReadOnlyList<string>>> UploadFilesAsync(string accountKey, string? parentKey, IReadOnlyList<string> paths, Func<string, Task<FileConflictChoice>> onConflict, IProgress<double>? progress, CancellationToken ct) => throw new PortPendingException("FileTransfer.UploadFilesAsync");

    public Task<Result<IReadOnlyList<string>>> UploadFolderAsync(string accountKey, string? parentKey, string path, Func<string, Task<FileConflictChoice>> onConflict, IProgress<double>? progress, CancellationToken ct) => throw new PortPendingException("FileTransfer.UploadFolderAsync");

    public Task<Result<string>> DownloadAsync(string accountKey, string key, string directory, CancellationToken ct) => throw new PortPendingException("FileTransfer.DownloadAsync");

    public Task<Result<string>> DownloadZipAsync(string accountKey, IReadOnlyList<string> keys, string directory, CancellationToken ct) => throw new PortPendingException("FileTransfer.DownloadZipAsync");

    public Task<Result<string>> CreateTextFileAsync(string accountKey, string? parentKey, string name, string text, CancellationToken ct) => throw new PortPendingException("FileTransfer.CreateTextFileAsync");

    public Task<Result<byte[]>> ReadAsync(string accountKey, string key, int maxBytes, CancellationToken ct) => throw new PortPendingException("FileTransfer.ReadAsync");
}
