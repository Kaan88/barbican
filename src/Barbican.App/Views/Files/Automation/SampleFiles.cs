#if AUTOMATION
// Mirrors: BARBICAN_SAMPLE=1 runs (PORT-PLAN 4.4): an in-memory file store, writer and transfer over sample folders and files, so the Files screen runs hidden with no server (files-data is not needed). Never used with a real account.
// The trash is a ".Trash" folder in the root with loose children (no dated batches) and a map from each trashed key to the folder it came from.

using System.Text;
using Barbican.Core.Common;
using Barbican.Core.Files;

#pragma warning disable MA0048 // the store, writer and transfer of one sample account share one class

namespace Barbican.App.Views.Files.Automation;

/// <summary>The sample file account.</summary>
public sealed class SampleFiles : IFileStore, IFileWriter, IFileTransfer
{
    public const string Account = "sample";

    private readonly List<FileEntry> _all = [];
    private readonly Dictionary<string, string?> _origin = new(StringComparer.Ordinal);
    private readonly List<string> _fav = [];
    private readonly List<string> _recent = [];
    private int _next = 100;

    public SampleFiles()
    {
        var when = new DateTimeOffset(2026, 11, 2, 9, 30, 0, TimeSpan.Zero);
        Dir("trash", null, FileTrash.TrashName);
        Dir("docs", null, "Documents");
        Dir("pics", null, "Pictures");
        Dir("work", "docs", "Work");
        Dir("proj", "work", "Barbican notes");
        Dir("sh", null, "Shared folder", shared: true);
        File("f1", null, "Welcome.txt", "text/plain", 412, when);
        File("f2", null, "Budget 2026.csv", "text/csv", 18_204, when.AddDays(3));
        File("f3", null, "Handbook.pdf", "application/pdf", 2_480_112, when.AddDays(9));
        File("f4", null, "Archive.zip", "application/zip", 9_880_001, when.AddDays(12));
        File("f5", "pics", "Sunset.png", "image/png", 540_221, when.AddDays(4));
        File("f6", "pics", "Logo.png", "image/png", 12_330, when.AddDays(6));
        File("f7", "docs", "Meeting notes.md", "text/markdown", 2_048, when.AddDays(7));
        File("f8", "docs", "Invoice.eml", "message/rfc822", 3_700, when.AddDays(8));
        File("f9", "work", "Plan.txt", "text/plain", 880, when.AddDays(10));
        File("f10", null, ".hidden-config", "text/plain", 20, when);
        File("f11", "proj", "Ideas.txt", "text/plain", 1_400, when.AddDays(11));
        _fav.Add("docs");
        _recent.AddRange(["f1", "f7"]);
    }

    public event EventHandler<FileChangedEventArgs>? Changed;

    public IReadOnlyList<string> Favourites => _fav;

    public IReadOnlyList<string> Recent => _recent;

    public FileSnapshot? Snapshot(string accountKey) => new(accountKey, [.. _all], null, "s");

    public Task<Result<FileSnapshot>> LoadAsync(string accountKey, CancellationToken ct) => Task.FromResult(Result<FileSnapshot>.Ok(Snapshot(accountKey)!));

    public Task<Result<Unit>> RefreshAsync(string accountKey, CancellationToken ct) => DoneAsync();

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

    public Task<Result<string>> CreateFolderAsync(string accountKey, string? parentKey, string name, CancellationToken ct)
    {
        var id = "n" + _next++;
        Dir(id, parentKey, name);
        return Task.FromResult(Result<string>.Ok(id));
    }

    public Task<Result<Unit>> RenameAsync(string accountKey, string key, string newName, CancellationToken ct) => EditAsync(key, e => e with { Name = newName });

    public Task<Result<Unit>> MoveAsync(string accountKey, IReadOnlyList<string> keys, string? destinationKey, CancellationToken ct)
    {
        foreach (var k in keys)
        {
            Replace(k, e => e with { ParentKey = destinationKey });
        }

        return NotifyAsync();
    }

    public Task<Result<Unit>> CopyAsync(string accountKey, IReadOnlyList<string> keys, string? destinationKey, CancellationToken ct)
    {
        foreach (var k in keys)
        {
            if (_all.Find(e => string.Equals(e.Key, k, StringComparison.Ordinal)) is { } e)
            {
                var id = "n" + _next++;
                _all.Add(e with { Key = id, Id = id, ParentKey = destinationKey });
            }
        }

        return NotifyAsync();
    }

    public Task<Result<Unit>> DuplicateAsync(string accountKey, string key, CancellationToken ct)
    {
        if (_all.Find(e => string.Equals(e.Key, key, StringComparison.Ordinal)) is { } e)
        {
            var id = "n" + _next++;
            _all.Add(e with { Key = id, Id = id, Name = "Copy of " + e.Name });
        }

        return NotifyAsync();
    }

    public Task<Result<Unit>> TrashAsync(string accountKey, IReadOnlyList<string> keys, CancellationToken ct)
    {
        foreach (var k in keys)
        {
            _origin[k] = _all.Find(e => string.Equals(e.Key, k, StringComparison.Ordinal))?.ParentKey;
            Replace(k, e => e with { ParentKey = "trash" });
        }

        return NotifyAsync();
    }

    public Task<Result<Unit>> RestoreAsync(string accountKey, IReadOnlyList<string> keys, CancellationToken ct)
    {
        foreach (var k in keys)
        {
            Replace(k, e => e with { ParentKey = _origin.GetValueOrDefault(k) });
        }

        return NotifyAsync();
    }

    public Task<Result<Unit>> PurgeAsync(string accountKey, IReadOnlyList<string> keys, CancellationToken ct)
    {
        _ = _all.RemoveAll(e => keys.Contains(e.Key, StringComparer.Ordinal));
        return NotifyAsync();
    }

    public Task<Result<Unit>> EmptyTrashAsync(string accountKey, CancellationToken ct)
    {
        _ = _all.RemoveAll(e => string.Equals(e.ParentKey, "trash", StringComparison.Ordinal));
        return NotifyAsync();
    }

    public Task<Result<Unit>> ShareAsync(string accountKey, string key, string principalId, bool? write, CancellationToken ct) => DoneAsync();

    public Task<Result<IReadOnlyList<string>>> UploadFilesAsync(string accountKey, string? parentKey, IReadOnlyList<string> paths, Func<string, Task<FileConflictChoice>> onConflict, IProgress<double>? progress, CancellationToken ct) =>
        UploadAsync(parentKey, paths, onConflict);

    public async Task<Result<IReadOnlyList<string>>> UploadFolderAsync(string accountKey, string? parentKey, string path, Func<string, Task<FileConflictChoice>> onConflict, IProgress<double>? progress, CancellationToken ct)
    {
        var id = "n" + _next++;
        Dir(id, parentKey, Path.GetFileName(path.TrimEnd('/', '\\')));
        var made = await UploadAsync(id, Directory.Exists(path) ? [.. Directory.GetFiles(path)] : [], onConflict).ConfigureAwait(true);
        Changed?.Invoke(this, new FileChangedEventArgs(Account, []));
        return made;
    }

    public Task<Result<string>> DownloadAsync(string accountKey, string key, string directory, CancellationToken ct) => Task.FromResult(Result<string>.Ok(Path.Combine(directory, key)));

    public Task<Result<string>> DownloadZipAsync(string accountKey, IReadOnlyList<string> keys, string directory, CancellationToken ct) => Task.FromResult(Result<string>.Ok(Path.Combine(directory, "files.zip")));

    public Task<Result<string>> CreateTextFileAsync(string accountKey, string? parentKey, string name, string text, CancellationToken ct)
    {
        var id = "n" + _next++;
        File(id, parentKey, name, "text/plain", text.Length, DateTimeOffset.UtcNow);
        Changed?.Invoke(this, new FileChangedEventArgs(Account, [id]));
        return Task.FromResult(Result<string>.Ok(id));
    }

    public Task<Result<byte[]>> ReadAsync(string accountKey, string key, int maxBytes, CancellationToken ct)
    {
        var e = _all.Find(x => string.Equals(x.Key, key, StringComparison.Ordinal));
        if (e is null)
        {
            return Task.FromResult(Result<byte[]>.Ok([]));
        }

        var type = FilesPreviewRulesRef(e);
        return Task.FromResult(Result<byte[]>.Ok(type switch
        {
            "eml" => Encoding.UTF8.GetBytes("From: Sample Sender <sender@example.org>\r\nTo: you@example.org\r\nSubject: Your invoice\r\nDate: Mon, 02 Nov 2026 09:30:00 +0000\r\n\r\nHello,\r\n\r\nthe invoice for November is attached to this sample message.\r\n"),
            "png" => Png(e.Name),
            _ => Encoding.UTF8.GetBytes("# " + e.Name + "\r\n\r\nSample content of this file.\r\nSecond line of the sample file.\r\n"),
        }));
    }

    private static string FilesPreviewRulesRef(FileEntry e) =>
        Barbican.Presentation.Files.FilesPreviewRules.KindOf(e.MediaType, e.Name) switch
        {
            Barbican.Presentation.Files.FilesPreviewKind.Eml => "eml",
            Barbican.Presentation.Files.FilesPreviewKind.Image => "png",
            _ => "text",
        };

    // a 64x48 gradient PNG, drawn per file name so two sample pictures differ
    private static byte[] Png(string name)
    {
        const int w = 64;
        const int h = 48;
        var seed = name.Length * 37 % 200;
        var raw = new List<byte>();
        for (var y = 0; y < h; y++)
        {
            raw.Add(0);
            for (var x = 0; x < w; x++)
            {
                raw.Add((byte)((x * 4) + seed % 60));
                raw.Add((byte)((y * 5) + 20));
                raw.Add((byte)(200 - (x * 2)));
            }
        }

        using var ms = new MemoryStream();
        ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        Chunk(ms, "IHDR", [0, 0, 0, w, 0, 0, 0, h, 8, 2, 0, 0, 0]);
        using var z = new MemoryStream();
        using (var d = new System.IO.Compression.ZLibStream(z, System.IO.Compression.CompressionLevel.Fastest, true))
        {
            d.Write(raw.ToArray());
        }

        Chunk(ms, "IDAT", z.ToArray());
        Chunk(ms, "IEND", []);
        return ms.ToArray();
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        Span<byte> len = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
        s.Write(len);
        var t = Encoding.ASCII.GetBytes(type);
        s.Write(t);
        s.Write(data);
        var c = 0xFFFFFFFFu;
        foreach (var b in t.Concat(data))
        {
            c ^= b;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? (c >> 1) ^ 0xEDB88320u : c >> 1;
            }
        }

        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(len, ~c);
        s.Write(len);
    }

    private async Task<Result<IReadOnlyList<string>>> UploadAsync(string? parentKey, IReadOnlyList<string> paths, Func<string, Task<FileConflictChoice>> onConflict)
    {
        var made = new List<string>();
        foreach (var p in paths)
        {
            var name = Path.GetFileName(p);
            var size = System.IO.File.Exists(p) ? new FileInfo(p).Length : 0;
            var clash = _all.Find(e => string.Equals(e.ParentKey, parentKey, StringComparison.Ordinal) && string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
            if (clash is not null)
            {
                switch (await onConflict(name).ConfigureAwait(true))
                {
                    case FileConflictChoice.Skip:
                        continue;
                    case FileConflictChoice.Replace:
                        _ = _all.Remove(clash);
                        break;
                    default:
                        name = Path.GetFileNameWithoutExtension(name) + " (1)" + Path.GetExtension(name);
                        break;
                }
            }

            var id = "n" + _next++;
            File(id, parentKey, name, "application/octet-stream", size, DateTimeOffset.UtcNow);
            made.Add(id);
        }

        Changed?.Invoke(this, new FileChangedEventArgs(Account, [.. made]));
        return Result<IReadOnlyList<string>>.Ok(made);
    }

    private void Dir(string id, string? parent, string name, bool shared = false) =>
        _all.Add(new FileEntry(id, Account, shared, id, parent, name, true, null, 0, null, null, null));

    private void File(string id, string? parent, string name, string type, long size, DateTimeOffset modified) =>
        _all.Add(new FileEntry(id, Account, false, id, parent, name, false, "b" + id, size, modified, type, null));

    private void Replace(string key, Func<FileEntry, FileEntry> f)
    {
        var i = _all.FindIndex(e => string.Equals(e.Key, key, StringComparison.Ordinal));
        if (i >= 0)
        {
            _all[i] = f(_all[i]);
        }
    }

    private Task<Result<Unit>> EditAsync(string key, Func<FileEntry, FileEntry> f)
    {
        Replace(key, f);
        return NotifyAsync();
    }

    private Task<Result<Unit>> NotifyAsync()
    {
        Changed?.Invoke(this, new FileChangedEventArgs(Account, []));
        return DoneAsync();
    }

    private static Task<Result<Unit>> DoneAsync() => Task.FromResult(Result<Unit>.Ok(default));
}
#endif
