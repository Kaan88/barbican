// Ported from Rust src/files.rs tests :779 trash_batch_names (T-091) and :790 trash_listing_and_names (T-092); M5 stream files-data.

#pragma warning disable MA0006, MA0002, CA1861 // ported Rust assertions compare string literals and literal arrays

using Barbican.Core.Files;
using static Barbican.Core.Tests.Files.FileTestData;

namespace Barbican.Core.Tests.Files;

public sealed class FileTrashTests
{
    [Fact]
    [RustTest("files::trash_batch_names")]
    public void Trash_batch_names()
    {
        Assert.Equal("1730112000000~b12", FileTrash.BatchName("b12", 1730112000000));
        Assert.Equal("1730112000000~", FileTrash.BatchName(null, 1730112000000));
        var withParent = FileTrash.ParseBatch("1730112000000~b12");
        Assert.Equal(1730112000000L, withParent!.Value.DeletedMs);
        Assert.Equal("b12", withParent.Value.Parent);
        var root = FileTrash.ParseBatch("1730112000000~");
        Assert.Equal(1730112000000L, root!.Value.DeletedMs);
        Assert.Null(root.Value.Parent);
        Assert.Null(FileTrash.ParseBatch("report.pdf"));
        Assert.Null(FileTrash.ParseBatch("123~x"));
        Assert.Null(FileTrash.ParseBatch("1730112000000~a/b"));
    }

    [Fact]
    [RustTest("files::trash_listing_and_names")]
    public void Trash_listing_and_names()
    {
        FileEntry[] nodes =
        [
            N("t", null, ".Trash", true), N("bt", "t", "1730112000000~d", true), N("f", "bt", "a.txt", false),
            N("loose", "t", "stray.txt", false), N("d", null, "Docs", true), N("g", "d", "keep.txt", false),
        ];
        var sub = FileTrash.Subtree(nodes);
        Assert.True(sub.Contains("t") && sub.Contains("bt") && sub.Contains("f") && sub.Contains("loose"));
        Assert.False(sub.Contains("d") || sub.Contains("g"));
        var items = FileTrash.Items(nodes);
        Assert.Equal(2, items.Count);
        var f = items.First(t => t.Entry.Id == "f");
        Assert.Equal("bt", f.BatchKey);
        Assert.Equal(1730112000000L, f.DeletedMs);
        Assert.Equal("d", f.FromParent);
        Assert.Equal(["bt"], FileTrash.EmptiedBatches(nodes, ["f"]));
        var used = new HashSet<string>(StringComparer.Ordinal) { "a.txt", "a (2).txt" };
        Assert.Equal("a (3).txt", FileNames.UniqueName("a.txt", used));
        Assert.Equal("b", FileNames.UniqueName("b", used));
    }
}
