// Mirrors: Bulwark lib/files-zip.ts (planZip :27: files as they are, folders with everything below them under their own names, empty folders kept, a symlink skipped, "name (1).ext" for a path used twice, separators in a name replaced; buildZip :84)
// and stores/file-store.ts downloadZip :1491; System.IO.Compression (a streaming writer: a file goes from the network into the archive without being held whole); RFC 8620 6.2. G-017. Hostile input: a node name is one path segment ("." and ".."
// are replaced, so no entry leaves the archive), the plan is bounded in entries and bytes, a cycle in the tree is cut. Owner files-data.

using System.IO.Compression;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Plugins.Hooks;

namespace Barbican.Core.Files;

public sealed partial class FileTransfer
{
    /// <summary>The most bytes one zip takes (D-M5-C: 2 GiB; the browser Bulwark builds it in memory, this one streams, but a download that large is almost certainly a mistake).</summary>
    public const long MaxZipBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>The most entries one zip takes.</summary>
    public const int MaxZipEntries = 100_000;

    /// <inheritdoc/>
    public async Task<Result<string>> DownloadZipAsync(string accountKey, IReadOnlyList<string> keys, string directory, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(directory);
        var opened = await FileContext.OpenAsync(_accounts, _store, accountKey, ct).ConfigureAwait(false);
        if (!opened.IsOk)
        {
            return Result<string>.Fail(opened.Error!);
        }

        var ctx = opened.Value!;
        var picked = ctx.TopLevel([.. keys.Select(ctx.Find).OfType<FileEntry>().DistinctBy(e => e.Key, StringComparer.Ordinal)]);
        if (picked.Count == 0)
        {
            return Result<string>.Fail(new ValidationError("There is nothing to download."));
        }

        var plan = Plan(ctx, picked);
        if (!plan.IsOk)
        {
            return Result<string>.Fail(plan.Error!);
        }

        var name = picked.Count == 1 && picked[0].IsDirectory ? picked[0].Name + ".zip" : "files.zip";
        var path = await Task.Run(() => FreePath(directory, FileNames.LocalName(name)), ct).ConfigureAwait(false);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var written = await WriteZipAsync(ctx, plan.Value!, temp, ct).ConfigureAwait(false);
            if (!written.IsOk)
            {
                return Result<string>.Fail(written.Error!);
            }

            File.Move(temp, path, overwrite: false);
        }
        catch (OperationCanceledException)
        {
            return Result<string>.Fail(new ValidationError("The download was cancelled."));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Result<string>.Fail(new ValidationError("Could not save the zip: " + e.Message));
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }

        foreach (var e in picked)
        {
            _hooks.Fire(HookNames.OnFileDownload, () => ctx.View(e));
        }

        return Result<string>.Ok(path);
    }

    // the archive to a file: empty folders, then each file streamed from the network into its entry
    private static async Task<Result<Unit>> WriteZipAsync(FileContext ctx, ZipPlan plan, string temp, CancellationToken ct)
    {
        var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await using (file.ConfigureAwait(false))
        using (var zip = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var folder in plan.EmptyFolders)
            {
                _ = zip.CreateEntry(folder + "/");
            }

            foreach (var (entryPath, node) in plan.Files)
            {
                ct.ThrowIfCancellationRequested();
                var download = await ctx.Client.DownloadAsync(ctx.AccountOf(node), node.BlobId!, FileNames.LocalName(node.Name), node.MediaType ?? "application/octet-stream", ct).ConfigureAwait(false);
                if (!download.IsOk)
                {
                    return Result<Unit>.Fail(download.Error!);
                }

                var source = download.Value!;
                await using var ownedSource = source.ConfigureAwait(false);
                var entry = zip.CreateEntry(entryPath, CompressionLevel.Fastest);
                if (node.Modified is { } m && m.Year is >= 1980 and <= 2107)
                {
                    entry.LastWriteTime = m;
                }

                var sink = await entry.OpenAsync(ct).ConfigureAwait(false);
                await using var ownedSink = sink.ConfigureAwait(false);
                await source.CopyToAsync(sink, ct).ConfigureAwait(false);
            }
        }

        return Result<Unit>.Ok(default);
    }

    /// <summary>What goes into the zip of some items: files as they are, folders with everything below them (Bulwark planZip).</summary>
    private static Result<ZipPlan> Plan(FileContext ctx, IReadOnlyList<FileEntry> picked)
    {
        var planner = new ZipPlanner(ctx);
        foreach (var e in picked)
        {
            planner.Add(e, string.Empty);
        }

        if (planner.TooMany)
        {
            return Result<ZipPlan>.Fail(new ValidationError("That is too many files for one zip."));
        }

        return planner.Plan.TotalBytes > MaxZipBytes ? Result<ZipPlan>.Fail(new ValidationError("That is too much data for one zip.")) : Result<ZipPlan>.Ok(planner.Plan);
    }

    private sealed class ZipPlanner(FileContext ctx)
    {
        private readonly Dictionary<string, List<FileEntry>> _byParent = ctx.Entries.Where(e => e.ParentKey is not null).GroupBy(e => e.ParentKey!, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> _usedByFolder = new(StringComparer.Ordinal);
        private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
        private int _count;

        public ZipPlan Plan { get; } = new();

        public bool TooMany { get; private set; }

        public void Add(FileEntry node, string prefix)
        {
            if (TooMany || !_seen.Add(node.Key))
            {
                return;
            }

            if (++_count > MaxZipEntries)
            {
                TooMany = true;
                return;
            }

            // a node without a blob that is not a folder (a symlink of Stalwart 1.0) has no content to pack
            if (!node.IsDirectory && node.BlobId is null)
            {
                return;
            }

            if (!_usedByFolder.TryGetValue(prefix, out var siblings))
            {
                _usedByFolder[prefix] = siblings = new HashSet<string>(StringComparer.Ordinal);
            }

            var segment = FileNames.UniqueName(FileNames.ZipSegment(node.Name), siblings);
            _ = siblings.Add(segment);
            var path = prefix + segment;
            if (!node.IsDirectory)
            {
                Plan.Files.Add((path, node));
                Plan.TotalBytes += node.Size;
                return;
            }

            var kids = _byParent.GetValueOrDefault(node.Key) ?? [];
            if (kids.Count == 0)
            {
                Plan.EmptyFolders.Add(path);
            }

            foreach (var k in kids)
            {
                Add(k, path + "/");
            }
        }
    }

    private sealed class ZipPlan
    {
        public List<(string Path, FileEntry Node)> Files { get; } = [];

        public List<string> EmptyFolders { get; } = [];

        public long TotalBytes { get; set; }
    }
}
