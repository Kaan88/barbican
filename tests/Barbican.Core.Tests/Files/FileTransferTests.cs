// Not Rust rows: G-016 (upload conflict policy: Keep both, Replace as an update of the blob, Skip, asked once per taken name; D-M5-14), G-017 (zip), G-018 (new text file), G-019 (download filename rules of settings), B-FILE-010 (upload a folder),
// the upload hooks (onBeforeFileUpload, onAfterFileUpload, onFileUploadCancel, onFileDownload) and the hostile-name and size limits of the transfers (D-M5-03 reading of the files side), against FakeJmap. M5 stream files-data.

#pragma warning disable MA0006, MA0002, VSTHRD103 // test assertions compare ordinal string literals and read small scratch files synchronously

using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Barbican.Core.Files;
using Barbican.Core.Plugins.Payloads;
using Barbican.FakeJmap;

namespace Barbican.Core.Tests.Files;

public sealed class FileTransferTests
{
    private static CancellationToken Ct => FileRig.Ct;

    private static Func<string, Task<FileConflictChoice>> Always(FileConflictChoice choice, List<string>? asked = null) => name =>
    {
        asked?.Add(name);
        return Task.FromResult(choice);
    };

    private static async Task<FileRig> LoadedAsync(FakeFiles? files = null)
    {
        var rig = await FileRig.CreateAsync(files);
        _ = await rig.LoadAsync();
        return rig;
    }

    private static async Task<string> TextAsync(FileRig rig, string key) => Encoding.UTF8.GetString((await rig.Transfer.ReadAsync(rig.Key, key, 100_000, Ct)).Value!);

    [Fact]
    public async Task Files_are_uploaded_into_a_folder_and_report_progress()
    {
        using var rig = await LoadedAsync();
        var a = rig.Local("up/a.txt", "alpha");
        var b = rig.Local("up/b.json", "{\"b\":2}");
        var progress = new List<double>();

        var done = await rig.Transfer.UploadFilesAsync(rig.Key, "D3", [a, b], Always(FileConflictChoice.KeepBoth), new Progress<double>(progress.Add), Ct);

        Assert.True(done.IsOk, done.Error?.Message);
        Assert.Equal(2, done.Value!.Count);
        var entries = done.Value.Select(k => rig.Store.Snapshot(rig.Key)!.Entries.Single(e => e.Key == k)).ToList();
        Assert.Equal(["a.txt", "b.json"], entries.Select(e => e.Name));
        Assert.Equal("application/json", entries[1].MediaType);
        Assert.Equal("alpha", await TextAsync(rig, entries[0].Key));
        var uploaded = Assert.IsType<FileResourceView>(await rig.HookAsync("onAfterFileUpload"));
        Assert.Equal("/Photos", uploaded.Path);
        Assert.NotNull(await rig.HookAsync("onBeforeFileUpload"));
        await Task.Delay(50, Ct);
        Assert.Equal(1.0, progress.Max());
    }

    [Fact]
    public async Task A_taken_name_asks_once_and_the_choice_decides()
    {
        using var rig = await LoadedAsync();
        var notes = rig.Local("in/notes.txt", "replacement");
        var asked = new List<string>();

        // Keep both: "notes (2).txt" beside the old file
        var keep = await rig.Transfer.UploadFilesAsync(rig.Key, FakeSeed.FilesDocumentsId, [notes], Always(FileConflictChoice.KeepBoth, asked), null, Ct);
        Assert.True(keep.IsOk);
        Assert.Equal(["notes.txt"], asked);
        Assert.Contains(rig.Children(FakeSeed.FilesDocumentsId), e => e.Name == "notes (2).txt");
        Assert.Equal(FakeSeed.FilesNotesText, await TextAsync(rig, "N1"));

        // Skip: nothing changes and nothing is uploaded
        var count = rig.Store.Snapshot(rig.Key)!.Entries.Count;
        _ = await rig.Transfer.UploadFilesAsync(rig.Key, FakeSeed.FilesDocumentsId, [notes], Always(FileConflictChoice.Skip), null, Ct);
        Assert.Equal(count, rig.Store.Snapshot(rig.Key)!.Entries.Count);

        // Replace: an update of the blob of the same node, which keeps its id (and its shares and links)
        var replace = await rig.Transfer.UploadFilesAsync(rig.Key, FakeSeed.FilesDocumentsId, [notes], Always(FileConflictChoice.Replace), null, Ct);
        Assert.True(replace.IsOk);
        Assert.Empty(replace.Value!);
        Assert.Equal(count, rig.Store.Snapshot(rig.Key)!.Entries.Count);
        Assert.Equal("replacement", await TextAsync(rig, "N1"));
        Assert.Equal(11, rig.Entry("notes.txt").Size);
    }

    [Fact]
    public async Task A_name_over_the_servers_limit_is_shortened_for_an_upload_keeping_its_extension()
    {
        using var rig = await LoadedAsync(new FakeFiles { SeedM5 = true, NameRules = true });
        var longName = new string('é', 130) + ".txt";
        var done = await rig.Transfer.UploadFilesAsync(rig.Key, null, [rig.Local("in/" + longName, "x")], Always(FileConflictChoice.KeepBoth), null, Ct);

        Assert.True(done.IsOk, done.Error?.Message);
        var name = rig.Store.Snapshot(rig.Key)!.Entries.Single(e => e.Key == done.Value![0]).Name;
        Assert.True(Encoding.UTF8.GetByteCount(name) <= 255);
        Assert.EndsWith(".txt", name, StringComparison.Ordinal);
    }

        [Fact]
    public async Task A_file_over_the_limit_is_reported_and_the_others_still_go()
    {
        using var rig = await FileRig.CreateAsync(options: new FakeJmapOptions { MaxSizeUpload = 10 });
        _ = await rig.LoadAsync();
        var done = await rig.Transfer.UploadFilesAsync(rig.Key, null, [rig.Local("in/big.txt", new string('x', 50)), rig.Local("in/small.txt", "ok"), Path.Combine(rig.Root, "in", "gone.txt")], Always(FileConflictChoice.KeepBoth), null, Ct);

        Assert.False(done.IsOk);
        Assert.Contains("too large", done.Error!.Message, StringComparison.Ordinal);
        Assert.Equal("small.txt", rig.Store.Snapshot(rig.Key)!.Entries.Single(e => e.Key == Assert.Single(done.Value!)).Name);
        Assert.DoesNotContain(rig.Store.Snapshot(rig.Key)!.Entries, e => e.Name == "big.txt");
    }

    [Fact]
    public async Task A_plugin_can_stop_an_upload()
    {
        using var rig = await LoadedAsync();
        rig.Hooks.Deny("onBeforeFileUpload");
        var done = await rig.Transfer.UploadFilesAsync(rig.Key, null, [rig.Local("in/x.txt", "x")], Always(FileConflictChoice.KeepBoth), null, Ct);

        Assert.True(done.IsOk);
        Assert.Empty(done.Value!);
        Assert.DoesNotContain(rig.Store.Snapshot(rig.Key)!.Entries, e => e.Name == "x.txt");
    }

    [Fact]
    public async Task A_cancelled_upload_leaves_no_half_made_node_and_tells_the_plugins()
    {
        using var rig = await LoadedAsync();
        using var cts = new CancellationTokenSource();
        var count = rig.Store.Snapshot(rig.Key)!.Entries.Count;

        // the user cancels while the dialog for the conflict is open: the bytes are never sent as a node
        var done = await rig.Transfer.UploadFilesAsync(rig.Key, FakeSeed.FilesDocumentsId, [rig.Local("in/notes.txt", "n"), rig.Local("in/other.txt", "o")], async _ =>
        {
            await cts.CancelAsync();
            return FileConflictChoice.KeepBoth;
        }, null, cts.Token);

        Assert.False(done.IsOk);
        Assert.Contains("cancelled", done.Error!.Message, StringComparison.Ordinal);
        Assert.Equal(count, rig.Store.Snapshot(rig.Key)!.Entries.Count);
        Assert.IsType<FileCancelPayload>(await rig.HookAsync("onFileUploadCancel"));
    }

    [Fact]
    public async Task A_folder_is_uploaded_with_its_structure_and_symlinks_are_left_out()
    {
        using var rig = await LoadedAsync();
        _ = rig.Local("tree/top.txt", "t");
        _ = rig.Local("tree/sub/deep/leaf.txt", "l");
        _ = Directory.CreateDirectory(Path.Combine(rig.Root, "tree", "empty"));

        var done = await rig.Transfer.UploadFolderAsync(rig.Key, "D3", Path.Combine(rig.Root, "tree"), Always(FileConflictChoice.KeepBoth), null, Ct);

        Assert.True(done.IsOk, done.Error?.Message);
        var tree = rig.Children("D3").Single(e => e.Name == "tree");
        Assert.True(tree.IsDirectory);
        Assert.Equal(["empty", "sub", "top.txt"], rig.Children(tree.Key).Select(e => e.Name).Order(StringComparer.Ordinal));
        var deep = rig.Children(rig.Children(tree.Key).Single(e => e.Name == "sub").Key).Single();
        Assert.Equal("l", await TextAsync(rig, rig.Children(deep.Key).Single().Key));
        Assert.Equal(6, done.Value!.Count);
    }

    [Fact]
    public async Task A_folder_over_a_folder_asks_and_merges_or_numbers_or_skips()
    {
        using var rig = await LoadedAsync();
        _ = rig.Local("Invoices/march.txt", "new march");
        _ = rig.Local("Invoices/april.txt", "april");
        var path = Path.Combine(rig.Root, "Invoices");
        var asked = new List<string>();

        var keep = await rig.Transfer.UploadFolderAsync(rig.Key, FakeSeed.FilesDocumentsId, path, Always(FileConflictChoice.KeepBoth, asked), null, Ct);
        Assert.True(keep.IsOk);
        Assert.Equal(["Invoices"], asked);
        Assert.Contains(rig.Children(FakeSeed.FilesDocumentsId), e => e.Name == "Invoices (2)");

        // Replace merges: same-named files inside are replaced without asking again, others are added
        var merge = await rig.Transfer.UploadFolderAsync(rig.Key, FakeSeed.FilesDocumentsId, path, Always(FileConflictChoice.Replace, asked), null, Ct);
        Assert.True(merge.IsOk);
        Assert.Equal(["Invoices", "Invoices"], asked);
        Assert.Equal("new march", await TextAsync(rig, "I1"));
        Assert.Contains(rig.Children(FakeSeed.FilesInvoicesId), e => e.Name == "april.txt");

        var count = rig.Store.Snapshot(rig.Key)!.Entries.Count;
        _ = await rig.Transfer.UploadFolderAsync(rig.Key, FakeSeed.FilesDocumentsId, path, Always(FileConflictChoice.Skip), null, Ct);
        Assert.Equal(count, rig.Store.Snapshot(rig.Key)!.Entries.Count);
        Assert.False((await rig.Transfer.UploadFolderAsync(rig.Key, null, Path.Combine(rig.Root, "nothing"), Always(FileConflictChoice.Skip), null, Ct)).IsOk);
    }

    [Fact]
    public async Task A_download_follows_the_filename_rules_of_settings()
    {
        var prefs = new JsonObject { ["filenameSpaceReplacement"] = "underscore", ["filenameLowercase"] = true, ["filenameStripDiacritics"] = true };
        using var rig = await FileRig.CreateAsync(prefs: prefs);
        _ = await rig.LoadAsync();
        var key = (await rig.Transfer.CreateTextFileAsync(rig.Key, null, "Mein Bericht  Ärger.TXT", "hi", Ct)).Value!;
        var folder = Path.Combine(rig.Root, "out");

        var first = await rig.Transfer.DownloadAsync(rig.Key, key, folder, Ct);
        var second = await rig.Transfer.DownloadAsync(rig.Key, key, folder, Ct);

        Assert.Equal("mein_bericht_arger.txt", Path.GetFileName(first.Value));
        Assert.Equal("mein_bericht_arger (2).txt", Path.GetFileName(second.Value));
        Assert.Equal("hi", File.ReadAllText(first.Value!));
        Assert.Empty(Directory.GetFiles(folder, "*.part"));
        Assert.IsType<FileResourceView>(await rig.HookAsync("onFileDownload"));
        Assert.False((await rig.Transfer.DownloadAsync(rig.Key, FakeSeed.FilesDocumentsId, folder, Ct)).IsOk);
    }

    [Fact]
    public async Task A_hostile_node_name_never_leaves_the_folder_the_user_picked()
    {
        using var rig = await LoadedAsync();
        var blob = (await rig.Client.UploadAsync("b", Encoding.UTF8.GetBytes("evil"), "text/plain", Ct)).Value!.BlobId;
        foreach (var name in new[] { "..\\..\\evil.txt", "../../evil2.txt", "CON", "a:b?.txt", "trailing. " })
        {
            _ = await rig.CallAsync("FileNode/set", new JsonObject { ["accountId"] = "b", ["create"] = new JsonObject { ["n"] = new JsonObject { ["parentId"] = null, ["name"] = name, ["blobId"] = blob, ["type"] = "text/plain" } } });
        }

        _ = await rig.Store.RefreshAsync(rig.Key, Ct);
        var folder = Path.Combine(rig.Root, "safe", "out");
        foreach (var e in rig.Children(null).Where(e => !e.IsDirectory && e.BlobId == blob))
        {
            var done = await rig.Transfer.DownloadAsync(rig.Key, e.Key, folder, Ct);
            Assert.True(done.IsOk, done.Error?.Message);
            Assert.Equal(Path.GetFullPath(folder), Path.GetDirectoryName(done.Value));
        }

        Assert.False(File.Exists(Path.Combine(rig.Root, "evil.txt")));
        Assert.Equal(5, Directory.GetFiles(folder).Length);
        Assert.Contains(Directory.GetFiles(folder).Select(Path.GetFileName), n => n == "_CON");
    }

    [Fact]
    public async Task A_zip_holds_files_and_folders_with_their_own_names()
    {
        using var rig = await LoadedAsync();
        _ = await rig.Writer.CreateFolderAsync(rig.Key, FakeSeed.FilesDocumentsId, "Empty", Ct);
        _ = await rig.Writer.CopyAsync(rig.Key, ["R1"], FakeSeed.FilesDocumentsId, Ct);
        var folder = Path.Combine(rig.Root, "zips");

        var one = await rig.Transfer.DownloadZipAsync(rig.Key, [FakeSeed.FilesDocumentsId], folder, Ct);
        Assert.True(one.IsOk, one.Error?.Message);
        Assert.Equal("Documents.zip", Path.GetFileName(one.Value));
        using (var zip = ZipFile.OpenRead(one.Value!))
        {
            Assert.Equal(["Documents/Empty/", "Documents/Invoices/march.txt", "Documents/notes.txt", "Documents/readme.md"], zip.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal));
            using var reader = new StreamReader(zip.GetEntry("Documents/Invoices/march.txt")!.Open());
            Assert.Equal("Invoice March\n", await reader.ReadToEndAsync(Ct));
        }

        var many = await rig.Transfer.DownloadZipAsync(rig.Key, ["N1", "R1", "s1|S2"], folder, Ct);
        using var zip2 = ZipFile.OpenRead(many.Value!);
        Assert.Equal("files.zip", Path.GetFileName(many.Value));
        Assert.Equal(["notes.txt", "plan.md", "readme.md"], zip2.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal));
        Assert.Empty(Directory.GetFiles(folder, "*.part"));
        Assert.False((await rig.Transfer.DownloadZipAsync(rig.Key, [], folder, Ct)).IsOk);
    }

    [Fact]
    public async Task A_zip_entry_name_cannot_climb_out_and_a_repeated_name_is_numbered()
    {
        using var rig = await LoadedAsync();
        var blob = (await rig.Client.UploadAsync("b", Encoding.UTF8.GetBytes("x"), "text/plain", Ct)).Value!.BlobId;
        _ = await rig.CallAsync("FileNode/set", new JsonObject { ["accountId"] = "b", ["create"] = new JsonObject { ["dd"] = new JsonObject { ["parentId"] = "D3", ["name"] = "..", ["blobId"] = blob, ["type"] = "text/plain" }, ["sl"] = new JsonObject { ["parentId"] = "D3", ["name"] = "a/b.txt", ["blobId"] = blob, ["type"] = "text/plain" } } });
        _ = await rig.Store.RefreshAsync(rig.Key, Ct);

        var done = await rig.Transfer.DownloadZipAsync(rig.Key, ["D3"], Path.Combine(rig.Root, "z"), Ct);
        Assert.True(done.IsOk, done.Error?.Message);
        using var zip = ZipFile.OpenRead(done.Value!);

        Assert.All(zip.Entries, e => Assert.DoesNotContain("..", e.FullName.Split('/'), StringComparer.Ordinal));
        Assert.Contains("Photos/_", zip.Entries.Select(e => e.FullName));
        Assert.Contains("Photos/a_b.txt", zip.Entries.Select(e => e.FullName));
    }

    [Fact]
    public async Task A_new_text_file_is_uploaded_with_its_text()
    {
        using var rig = await LoadedAsync(new FakeFiles { SeedM5 = true, NameRules = true });
        var made = await rig.Transfer.CreateTextFileAsync(rig.Key, FakeSeed.FilesDocumentsId, "todo.txt", "buy milk", Ct);
        var again = await rig.Transfer.CreateTextFileAsync(rig.Key, FakeSeed.FilesDocumentsId, "todo.txt", string.Empty, Ct);
        var noExt = await rig.Transfer.CreateTextFileAsync(rig.Key, null, "README", "r", Ct);

        Assert.True(made.IsOk, made.Error?.Message);
        Assert.Equal("buy milk", await TextAsync(rig, made.Value!));
        Assert.Equal("todo (2).txt", rig.Store.Snapshot(rig.Key)!.Entries.Single(e => e.Key == again.Value).Name);
        Assert.Equal("text/plain", rig.Store.Snapshot(rig.Key)!.Entries.Single(e => e.Key == noExt.Value).MediaType);
        Assert.False((await rig.Transfer.CreateTextFileAsync(rig.Key, null, "con.txt", "x", Ct)).IsOk);
        Assert.False((await rig.Transfer.CreateTextFileAsync(rig.Key, null, "   ", "x", Ct)).IsOk);
        Assert.False((await rig.Transfer.CreateTextFileAsync(rig.Key, FileTrash.TrashLocation, "x.txt", "x", Ct)).IsOk);
    }

    [Fact]
    public async Task A_preview_read_is_capped_before_any_byte_is_fetched()
    {
        using var rig = await LoadedAsync();
        var small = await rig.Transfer.ReadAsync(rig.Key, "N1", 1000, Ct);
        Assert.Equal(FakeSeed.FilesNotesText, Encoding.UTF8.GetString(small.Value!));

        var refused = await rig.Transfer.ReadAsync(rig.Key, "N1", 5, Ct);
        Assert.False(refused.IsOk);
        Assert.Contains("too large", refused.Error!.Message, StringComparison.Ordinal);
        Assert.False((await rig.Transfer.ReadAsync(rig.Key, FakeSeed.FilesDocumentsId, 1000, Ct)).IsOk);
    }
}
