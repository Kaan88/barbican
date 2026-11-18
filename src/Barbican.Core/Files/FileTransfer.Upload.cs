// Mirrors: Rust files.rs:387 upload (blob upload to the node's account, then FileNode/set create with the media type), :546 upload_folder (the top folder first, each directory created before what is inside it, symlinks skipped, failures counted),
// Bulwark stores/file-store.ts (uploadFile :1108 with onBeforeFileUpload / onFileUploadCancel :1216, uploadFolder :1239), lib/files-upload-conflicts.ts and components/files/upload-conflict-dialog.tsx (a file over a file: Replace updates the blob of
// the node so it keeps its id, shares and links; Keep both makes "name (2).ext"; Skip; a folder over a folder: Replace merges, Keep both makes "Folder (2)"; a file and a folder of one name are kept side by side without asking),
// lib/file-name-rules.ts acceptedFileName (a local name is made acceptable to the server's rules); RFC 8620 6.1; Stalwart crates/jmap/src/file/set.rs. D-M5-14, G-016. Owner files-data.

using System.Globalization;
using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Mail.Write;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;

namespace Barbican.Core.Files;

public sealed partial class FileTransfer
{
    /// <summary>The deepest local folder an upload follows, and the most entries it reads (a hostile or looping tree must end).</summary>
    public const int MaxFolderDepth = 64;

    /// <summary>The most files and folders one folder upload takes.</summary>
    public const int MaxFolderEntries = 100_000;

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<string>>> UploadFilesAsync(string accountKey, string? parentKey, IReadOnlyList<string> paths, Func<string, Task<FileConflictChoice>> onConflict, IProgress<double>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(onConflict);
        var opened = await FileContext.OpenAsync(_accounts, _store, accountKey, ct).ConfigureAwait(false);
        if (!opened.IsOk)
        {
            return Result<IReadOnlyList<string>>.Fail(opened.Error!);
        }

        var ctx = opened.Value!;
        var target = ctx.Target(parentKey);
        if (!target.IsOk)
        {
            return Result<IReadOnlyList<string>>.Fail(target.Error!);
        }

        var (account, parentId) = target.Value;
        var files = paths.Select(p => new FileInfo(p)).ToList();
        var total = await Task.Run(() => files.Sum(f => f.Exists ? f.Length : 0), CancellationToken.None).ConfigureAwait(false);
        var run = new Run(ctx, account, onConflict, progress, total);
        var folder = Folder.Of(ctx, parentKey, parentId, FilePaths.Of(ctx.Entries, parentKey), autoReplace: false);
        foreach (var file in files)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            await UploadOneAsync(run, file, folder, ct).ConfigureAwait(false);
        }

        return await FinishAsync(run, accountKey, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<string>>> UploadFolderAsync(string accountKey, string? parentKey, string path, Func<string, Task<FileConflictChoice>> onConflict, IProgress<double>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(onConflict);
        var opened = await FileContext.OpenAsync(_accounts, _store, accountKey, ct).ConfigureAwait(false);
        if (!opened.IsOk)
        {
            return Result<IReadOnlyList<string>>.Fail(opened.Error!);
        }

        var ctx = opened.Value!;
        var target = ctx.Target(parentKey);
        if (!target.IsOk)
        {
            return Result<IReadOnlyList<string>>.Fail(target.Error!);
        }

        var root = new DirectoryInfo(path);
        if (!await Task.Run(() => root.Exists, CancellationToken.None).ConfigureAwait(false))
        {
            return Result<IReadOnlyList<string>>.Fail(new ValidationError("That folder is not there."));
        }

        var (account, parentId) = target.Value;
        var tree = await Task.Run(() => ReadTree(root), CancellationToken.None).ConfigureAwait(false);
        if (tree is null)
        {
            return Result<IReadOnlyList<string>>.Fail(new ValidationError("That folder has too many files to upload at once."));
        }

        var run = new Run(ctx, account, onConflict, progress, tree.TotalBytes);
        var parent = Folder.Of(ctx, parentKey, parentId, FilePaths.Of(ctx.Entries, parentKey), autoReplace: false);
        var top = FileNameChecks.AcceptedName(root.Name, ctx.Snapshot.Rules);

        // the top folder: a name taken by a folder asks (Replace merges, Keep both numbers it, Skip leaves it out); a name taken by a file is kept beside without asking
        Folder? into;
        if (parent.Existing.TryGetValue(top, out var taken) && taken.IsDirectory)
        {
            var choice = await onConflict(top).ConfigureAwait(false);
            into = choice switch
            {
                FileConflictChoice.Skip => null,
                FileConflictChoice.Replace => Folder.Of(ctx, taken.Key, taken.Id, parent.Path.TrimEnd('/') + "/" + taken.Name, autoReplace: true),
                _ => await MakeFolderAsync(run, parent, FileNames.UniqueName(top, parent.Used), ct).ConfigureAwait(false),
            };
        }
        else
        {
            into = await MakeFolderAsync(run, parent, FileNames.UniqueName(top, parent.Used), ct).ConfigureAwait(false);
        }

        if (into is not null)
        {
            await UploadTreeAsync(run, tree, into, ct).ConfigureAwait(false);
        }

        return await FinishAsync(run, accountKey, ct).ConfigureAwait(false);
    }

    // a folder, then its files, then its subfolders (each created before what is inside it)
    private async Task UploadTreeAsync(Run run, Tree tree, Folder into, CancellationToken ct)
    {
        foreach (var file in tree.Files)
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            await UploadOneAsync(run, file, into, ct).ConfigureAwait(false);
        }

        foreach (var (dir, sub) in tree.Folders)
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            var name = FileNameChecks.AcceptedName(dir.Name, run.Ctx.Snapshot.Rules);

            // inside a merged folder a folder of the same name is merged too, a file of that name is kept beside
            var next = into.Existing.TryGetValue(name, out var existing) && existing.IsDirectory
                ? Folder.Of(run.Ctx, existing.Key, existing.Id, into.Path.TrimEnd('/') + "/" + existing.Name, autoReplace: true)
                : await MakeFolderAsync(run, into, FileNames.UniqueName(name, into.Used), ct).ConfigureAwait(false);
            if (next is not null)
            {
                await UploadTreeAsync(run, sub, next, ct).ConfigureAwait(false);
            }
        }
    }

    private async Task<Folder?> MakeFolderAsync(Run run, Folder parent, string name, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return null;
        }

        var made = await FileJmap.CreateAsync(run.Ctx.Client, run.Ctx.Draft, run.Account, parent.RawId, name, null, null, CancellationToken.None).ConfigureAwait(false);
        if (!made.IsOk)
        {
            run.Failures.Add($"{name}: {made.Error!.Message}");
            return null;
        }

        _ = parent.Used.Add(name);
        var key = FileEntry.MakeKey(run.Account, made.Value!, !string.Equals(run.Account, run.Ctx.Primary, StringComparison.Ordinal));
        run.Created.Add(key);
        var path = parent.Path.TrimEnd('/') + "/" + name;
        _hooks.Fire(HookNames.OnDirectoryCreate, () => new FileResourceView(key, name, "directory", 0, string.Empty, parent.Path, null));
        return Folder.Of(run.Ctx, key, made.Value!, path, autoReplace: false, empty: true);
    }

    // one local file into a folder: the hooks, the conflict, the blob, then the node; a failure is noted and the next file goes on
    private async Task UploadOneAsync(Run run, FileInfo file, Folder folder, CancellationToken ct)
    {
        var (exists, length) = await Task.Run(() => (file.Exists, file.Exists ? file.Length : 0), CancellationToken.None).ConfigureAwait(false);
        var name = FileNameChecks.AcceptedName(file.Name, run.Ctx.Snapshot.Rules);
        var mime = FileMimeTypes.For(name);
        var limit = run.Ctx.Client.Session.Limits.MaxSizeUpload;
        var refusal = !exists ? "it is not there any more." : length > limit ? $"too large (the server takes up to {(limit < 1_000_000 ? limit + " bytes" : limit / 1_000_000 + " MB")})." : null;
        if (refusal is not null)
        {
            run.Failures.Add($"{file.Name}: {refusal}");
            run.Advance(length);
            return;
        }

        if (!await _hooks.AllowAsync(HookNames.OnBeforeFileUpload, () => new FileUploadPayload(name, length, mime, folder.Path), ct).ConfigureAwait(false))
        {
            run.Advance(length);
            return;
        }

        var plan = await PlanNameAsync(run, name, folder).ConfigureAwait(false);
        if (plan.Skip)
        {
            run.Advance(length);
            return;
        }

        if (await SendBlobAsync(run, file, mime, ct).ConfigureAwait(false) is not { } blob)
        {
            return;
        }

        // from here the node is made whole or not at all: this request is not cancelled half way
        var key = plan.Replace is not null
            ? await ReplaceAsync(run, plan.Replace, blob.BlobId, mime).ConfigureAwait(false)
            : await CreateAsync(run, folder, plan.Name, blob.BlobId, mime).ConfigureAwait(false);
        run.Advance(length);
        if (!key.IsOk)
        {
            run.Failures.Add($"{file.Name}: {key.Error!.Message}");
            return;
        }

        _ = folder.Used.Add(plan.Name);
        if (plan.Replace is null)
        {
            run.Created.Add(key.Value!);
        }

        var uploaded = new FileResourceView(key.Value!, plan.Name, "file", length, mime, folder.Path, _time.GetUtcNow().ToString("o", CultureInfo.InvariantCulture));
        _hooks.Fire(HookNames.OnAfterFileUpload, () => uploaded);
    }

    // the user's choice for a name taken by a file; a folder of that name is kept beside without asking; inside a merged folder a file is replaced
    private static async Task<NamePlan> PlanNameAsync(Run run, string name, Folder folder)
    {
        if (!folder.Existing.TryGetValue(name, out var taken))
        {
            return new NamePlan(false, folder.Used.Contains(name) ? FileNames.UniqueName(name, folder.Used) : name, null);
        }

        if (taken.IsDirectory)
        {
            return new NamePlan(false, FileNames.UniqueName(name, folder.Used), null);
        }

        var choice = folder.AutoReplace ? FileConflictChoice.Replace : await run.OnConflict(name).ConfigureAwait(false);
        return choice switch
        {
            FileConflictChoice.Skip => new NamePlan(true, name, null),
            FileConflictChoice.Replace => new NamePlan(false, name, taken),
            _ => new NamePlan(false, FileNames.UniqueName(name, folder.Used), null),
        };
    }

    // the bytes of a local file up as a blob; null when it failed or the user cancelled (already noted in the run)
    private static async Task<UploadedBlob?> SendBlobAsync(Run run, FileInfo file, string mime, CancellationToken ct)
    {
        Result<UploadedBlob> blob;
        try
        {
            var bytes = await File.ReadAllBytesAsync(file.FullName, ct).ConfigureAwait(false);
            blob = await new MailWriter(run.Ctx.Client, run.Account).UploadAsync(bytes, mime, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            run.CancelledAt ??= file.Name;
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            run.Failures.Add($"{file.Name}: {e.Message}");
            run.Advance(file.Length);
            return null;
        }

        // cancelled with the bytes up but no node made: the blob is left for the server to forget
        if (ct.IsCancellationRequested)
        {
            run.CancelledAt ??= file.Name;
            return null;
        }

        if (blob.IsOk)
        {
            return blob.Value;
        }

        run.Failures.Add($"{file.Name}: {blob.Error!.Message}");
        run.Advance(file.Length);
        return null;
    }

    private static async Task<Result<string>> CreateAsync(Run run, Folder folder, string name, string blobId, string mime)
    {
        var made = await FileJmap.CreateAsync(run.Ctx.Client, run.Ctx.Draft, run.Account, folder.RawId, name, blobId, mime, CancellationToken.None).ConfigureAwait(false);
        return made.IsOk ? Result<string>.Ok(FileEntry.MakeKey(run.Account, made.Value!, !string.Equals(run.Account, run.Ctx.Primary, StringComparison.Ordinal))) : Result<string>.Fail(made.Error!);
    }

    // Replace is an update of the blob of the node (it keeps its id, shares and links), never a delete and create (D-M5-14)
    private static async Task<Result<string>> ReplaceAsync(Run run, FileEntry existing, string blobId, string mime)
    {
        var patch = new JsonObject { ["blobId"] = blobId, [run.Ctx.Draft ? "mediaType" : "type"] = mime };
        var set = FileJmap.Whole(await FileJmap.SetAsync(run.Ctx.Client, run.Account, new JsonObject { ["update"] = new JsonObject { [existing.Id] = patch } }, CancellationToken.None).ConfigureAwait(false));
        return set.IsOk ? Result<string>.Ok(existing.Key) : Result<string>.Fail(set.Error!);
    }

    private async Task<Result<IReadOnlyList<string>>> FinishAsync(Run run, string accountKey, CancellationToken ct)
    {
        _ = await _store.RefreshAsync(accountKey, CancellationToken.None).ConfigureAwait(false);
        run.Progress?.Report(run.TotalBytes == 0 ? 1 : Math.Min(1, (double)run.Done / run.TotalBytes));
        if (run.CancelledAt is not null || ct.IsCancellationRequested)
        {
            var at = run.CancelledAt ?? string.Empty;
            _hooks.Fire(HookNames.OnFileUploadCancel, () => new FileCancelPayload(at, string.Empty));
            return Result<IReadOnlyList<string>>.FailPartial(new ValidationError("The upload was cancelled."), run.Created);
        }

        return run.Failures.Count == 0
            ? Result<IReadOnlyList<string>>.Ok(run.Created)
            : Result<IReadOnlyList<string>>.FailPartial(new ValidationError(run.Failures.Count == 1 ? run.Failures[0] : $"{run.Failures[0]} ({run.Failures.Count} uploads failed)"), run.Created);
    }

    // every directory and file below a local folder, symlinks and reparse points left out, bounded in depth and count; null when over the count
    private static Tree? ReadTree(DirectoryInfo root)
    {
        var budget = MaxFolderEntries;
        return Read(root, 0, ref budget);

        static Tree? Read(DirectoryInfo dir, int depth, ref int budget)
        {
            var tree = new Tree();
            if (depth > MaxFolderDepth)
            {
                return tree;
            }

            foreach (var entry in dir.EnumerateFileSystemInfos().OrderBy(e => e.Name, StringComparer.Ordinal))
            {
                if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint) || entry.LinkTarget is not null)
                {
                    continue;
                }

                if (--budget < 0)
                {
                    return null;
                }

                if (entry is DirectoryInfo sub)
                {
                    if (Read(sub, depth + 1, ref budget) is not { } inner)
                    {
                        return null;
                    }

                    tree.Folders.Add((sub, inner));
                }
                else if (entry is FileInfo f)
                {
                    tree.Files.Add(f);
                    tree.TotalBytes += f.Length;
                }
            }

            foreach (var (_, inner) in tree.Folders)
            {
                tree.TotalBytes += inner.TotalBytes;
            }

            return tree;
        }
    }

    private sealed record NamePlan(bool Skip, string Name, FileEntry? Replace);

    private sealed class Tree
    {
        public List<FileInfo> Files { get; } = [];

        public List<(DirectoryInfo Dir, Tree Tree)> Folders { get; } = [];

        public long TotalBytes { get; set; }
    }

    /// <summary>The names in the folder an upload goes into.</summary>
    private sealed class Folder
    {
        public string? Key { get; private init; }

        public string? RawId { get; private init; }

        public string Path { get; private init; } = "/";

        public bool AutoReplace { get; private init; }

        public Dictionary<string, FileEntry> Existing { get; private init; } = new(StringComparer.Ordinal);

        public HashSet<string> Used { get; private init; } = new(StringComparer.Ordinal);

        public static Folder Of(FileContext ctx, string? key, string? rawId, string path, bool autoReplace, bool empty = false)
        {
            List<FileEntry> children = empty ? [] : ctx.Entries.Where(e => string.Equals(e.ParentKey, key, StringComparison.Ordinal) && (key is not null || !e.Shared)).ToList();
            return new Folder
            {
                Key = key,
                RawId = rawId,
                Path = path,
                AutoReplace = autoReplace,
                Existing = children.GroupBy(e => e.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal),
                Used = new HashSet<string>(children.Select(e => e.Name), StringComparer.Ordinal),
            };
        }
    }

    private sealed class Run(FileContext ctx, string account, Func<string, Task<FileConflictChoice>> onConflict, IProgress<double>? progress, long totalBytes)
    {
        public FileContext Ctx { get; } = ctx;

        public string Account { get; } = account;

        public Func<string, Task<FileConflictChoice>> OnConflict { get; } = onConflict;

        public IProgress<double>? Progress { get; } = progress;

        public long TotalBytes { get; } = totalBytes;

        public long Done { get; private set; }

        public List<string> Created { get; } = [];

        public List<string> Failures { get; } = [];

        public string? CancelledAt { get; set; }

        public void Advance(long bytes)
        {
            Done += bytes;
            Progress?.Report(TotalBytes == 0 ? 0 : Math.Min(1, (double)Done / TotalBytes));
        }
    }
}
