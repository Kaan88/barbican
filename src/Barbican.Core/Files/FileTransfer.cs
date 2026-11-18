// Mirrors: Rust files.rs:355 download (the blob of a node from its own account, to a path), :381 upload (the blob then FileNode/set), Bulwark stores/file-store.ts (downloadFile :1481 with onFileDownload, createTextFile / new-file-dialog.tsx,
// readFile for the preview), lib/download-filename.ts (the filename rules of settings: filenameSpaceReplacement, filenameLowercase, filenameStripDiacritics, filenameCollapseSeparators), lib/download-guard.ts; RFC 8620 6.1 (upload) and 6.2 (download);
// Stalwart crates/jmap/src/blob/{upload,download}.rs. G-016..G-019. Hostile input: a node name is sanitised before it is a path, nothing is written outside the folder the user picked, a download goes to a temp file and is moved in, a read is capped. Owner files-data.

using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Mail.Write;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;
using Barbican.Core.Storage;

namespace Barbican.Core.Files;

/// <summary>The default <see cref="IFileTransfer"/>.</summary>
public sealed partial class FileTransfer : IFileTransfer
{
    private readonly IAccountManager _accounts;
    private readonly IFileStore _store;
    private readonly IHookBus _hooks;
    private readonly IPrefsStore? _prefs;
    private readonly TimeProvider _time;

    public FileTransfer(IAccountManager accounts, IFileStore store, IHookBus hooks, IPrefsStore? prefs = null, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(hooks);
        _accounts = accounts;
        _store = store;
        _hooks = hooks;
        _prefs = prefs;
        _time = time ?? TimeProvider.System;
    }

    /// <inheritdoc/>
    public async Task<Result<string>> DownloadAsync(string accountKey, string key, string directory, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(directory);
        var opened = await FileContext.OpenAsync(_accounts, _store, accountKey, ct).ConfigureAwait(false);
        if (!opened.IsOk)
        {
            return Result<string>.Fail(opened.Error!);
        }

        var ctx = opened.Value!;
        if (ctx.Find(key) is not { } entry || entry.IsDirectory || entry.BlobId is null)
        {
            return Result<string>.Fail(new ValidationError("There is no file to download."));
        }

        var path = await Task.Run(() => FreePath(directory, LocalNameFor(entry.Name)), ct).ConfigureAwait(false);
        var saved = await SaveBlobAsync(ctx, entry, path, ct).ConfigureAwait(false);
        if (!saved.IsOk)
        {
            return Result<string>.Fail(saved.Error!);
        }

        _hooks.Fire(HookNames.OnFileDownload, () => ctx.View(entry));
        return Result<string>.Ok(path);
    }

    /// <inheritdoc/>
    public async Task<Result<byte[]>> ReadAsync(string accountKey, string key, int maxBytes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);
        var opened = await FileContext.OpenAsync(_accounts, _store, accountKey, ct).ConfigureAwait(false);
        if (!opened.IsOk)
        {
            return Result<byte[]>.Fail(opened.Error!);
        }

        var ctx = opened.Value!;
        if (ctx.Find(key) is not { } entry || entry.IsDirectory || entry.BlobId is null)
        {
            return Result<byte[]>.Fail(new ValidationError("There is no file to read."));
        }

        // refused on the size the server reports before any byte is fetched, and again on what arrives (a server may lie)
        if (entry.Size > maxBytes)
        {
            return Result<byte[]>.Fail(new ValidationError("This file is too large to preview."));
        }

        var download = await ctx.Client.DownloadAsync(ctx.AccountOf(entry), entry.BlobId, FileNames.LocalName(entry.Name), entry.MediaType ?? "application/octet-stream", ct).ConfigureAwait(false);
        if (!download.IsOk)
        {
            return Result<byte[]>.Fail(download.Error!);
        }

        var stream = download.Value!;
        await using var owned = stream.ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                return Result<byte[]>.Fail(new ValidationError("This file is too large to preview."));
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), ct).ConfigureAwait(false);
        }

        return Result<byte[]>.Ok(buffer.ToArray());
    }

    /// <inheritdoc/>
    public async Task<Result<string>> CreateTextFileAsync(string accountKey, string? parentKey, string name, string text, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(text);
        var opened = await FileContext.OpenAsync(_accounts, _store, accountKey, ct).ConfigureAwait(false);
        if (!opened.IsOk)
        {
            return Result<string>.Fail(opened.Error!);
        }

        var ctx = opened.Value!;
        var target = ctx.Target(parentKey);
        if (!target.IsOk)
        {
            return Result<string>.Fail(target.Error!);
        }

        var clean = name.Trim();
        if (clean.Length == 0)
        {
            return Result<string>.Fail(new ValidationError("A name is needed."));
        }

        if (FileNameChecks.ProblemOf(clean, ctx.Snapshot.Rules) is { } problem)
        {
            return Result<string>.Fail(new ValidationError(FileNameChecks.Message(problem)));
        }

        var (account, parentId) = target.Value;
        var unique = FileNames.UniqueName(clean, ctx.Names(parentKey, account));
        var bytes = new System.Text.UTF8Encoding(false).GetBytes(text);
        var guessed = FileMimeTypes.For(unique);
        var mime = string.Equals(guessed, "application/octet-stream", StringComparison.Ordinal) ? "text/plain" : guessed;
        var path = FilePaths.Of(ctx.Entries, parentKey);
        if (!await _hooks.AllowAsync(HookNames.OnBeforeFileUpload, () => new FileUploadPayload(unique, bytes.Length, mime, path), ct).ConfigureAwait(false))
        {
            return Result<string>.Fail(new ValidationError("A plugin stopped the upload."));
        }

        var blob = await new MailWriter(ctx.Client, account).UploadAsync(bytes, mime, ct).ConfigureAwait(false);
        if (!blob.IsOk)
        {
            return Result<string>.Fail(blob.Error!);
        }

        var made = await FileJmap.CreateAsync(ctx.Client, ctx.Draft, account, parentId, unique, blob.Value!.BlobId, mime, CancellationToken.None).ConfigureAwait(false);
        if (!made.IsOk)
        {
            return Result<string>.Fail(made.Error!);
        }

        var key = FileEntry.MakeKey(account, made.Value!, !string.Equals(account, ctx.Primary, StringComparison.Ordinal));
        _ = await _store.RefreshAsync(accountKey, ct).ConfigureAwait(false);
        _hooks.Fire(HookNames.OnAfterFileUpload, () => new FileResourceView(key, unique, "file", bytes.Length, mime, path, _time.GetUtcNow().ToString("o", System.Globalization.CultureInfo.InvariantCulture)));
        return Result<string>.Ok(key);
    }

    /// <summary>The name a file gets on this disk: the filename rules of settings (G-019), then what a file system cannot take.</summary>
    internal string LocalNameFor(string name)
    {
        var p = _prefs?.Current;
        var rules = FileNames.DownloadName(
            name,
            p?.Str("filenameSpaceReplacement", "keep") ?? "keep",
            p?.Bool("filenameLowercase", false) ?? false,
            p?.Bool("filenameStripDiacritics", false) ?? false,
            p?.Bool("filenameCollapseSeparators", true) ?? true);

        // the rules keep the extension of "report.pdf" but a name whose stem the sanitiser empties falls back to the original text, cleaned for the disk
        return FileNames.LocalName(rules);
    }

    /// <summary>A path in <paramref name="directory"/> for <paramref name="name"/> that does not exist yet ("name (2).ext" otherwise); always inside the folder.</summary>
    internal static string FreePath(string directory, string name)
    {
        var dir = Path.GetFullPath(directory);
        var taken = new HashSet<string>(Directory.Exists(dir) ? Directory.EnumerateFileSystemEntries(dir).Select(Path.GetFileName).OfType<string>() : [], StringComparer.OrdinalIgnoreCase);
        var path = Path.GetFullPath(Path.Combine(dir, FileNames.UniqueName(name, taken)));
        return string.Equals(Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(path) ?? string.Empty), Path.TrimEndingDirectorySeparator(dir), StringComparison.OrdinalIgnoreCase)
            ? path
            : Path.Combine(dir, "file");
    }

    // the blob to a temp file next to the target, then moved in: an interrupted download leaves nothing under the real name
    private static async Task<Result<Unit>> SaveBlobAsync(FileContext ctx, FileEntry entry, string path, CancellationToken ct)
    {
        var download = await ctx.Client.DownloadAsync(ctx.AccountOf(entry), entry.BlobId!, FileNames.LocalName(entry.Name), entry.MediaType ?? "application/octet-stream", ct).ConfigureAwait(false);
        if (!download.IsOk)
        {
            return Result<Unit>.Fail(download.Error!);
        }

        var temp = path + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var source = download.Value!;
            await using var owned = source.ConfigureAwait(false);
            var target = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await using (target.ConfigureAwait(false))
            {
                await source.CopyToAsync(target, ct).ConfigureAwait(false);
                await target.FlushAsync(ct).ConfigureAwait(false);
            }

            File.Move(temp, path, overwrite: false);
            return Result<Unit>.Ok(default);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Result<Unit>.Fail(new ValidationError("Could not save the file: " + e.Message));
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }
}
