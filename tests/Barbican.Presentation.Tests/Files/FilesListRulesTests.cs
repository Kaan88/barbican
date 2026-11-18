// Not Rust rows: G-015 (sort, hidden files), G-013 (preview kinds), B-FILE-005. M5 stream files-ui.

#pragma warning disable MA0006, MA0002, CA1861 // ported Rust assertions compare string literals and literal arrays

using Barbican.Core.Files;
using Barbican.Presentation.Files;

namespace Barbican.Presentation.Tests.Files;

public sealed class FilesListRulesTests
{
    private static FileEntry E(string name, bool dir, long size = 0, string? type = null, long modified = 0) =>
        new(name, "a", false, name, null, name, dir, dir ? null : "b", size, DateTimeOffset.FromUnixTimeSeconds(modified), type, null);

    [Fact]
    public void Sorting_puts_folders_first_and_orders_names_naturally()
    {
        FileEntry[] entries = [E("file10.txt", false), E("file2.txt", false), E("Zeta", true), E("alpha", true), E("File1.txt", false)];
        Assert.Equal(["alpha", "Zeta", "File1.txt", "file2.txt", "file10.txt"], FilesListRules.Sort(entries, FilesSortKey.Name, false).Select(e => e.Name));
        Assert.Equal(["Zeta", "alpha", "file10.txt", "file2.txt", "File1.txt"], FilesListRules.Sort(entries, FilesSortKey.Name, true).Select(e => e.Name));
        FileEntry[] sized = [E("b", false, 5), E("a", false, 9), E("d", true, 1)];
        Assert.Equal(["d", "b", "a"], FilesListRules.Sort(sized, FilesSortKey.Size, false).Select(e => e.Name));
        Assert.Equal(["d", "a", "b"], FilesListRules.Sort(sized, FilesSortKey.Size, true).Select(e => e.Name));
    }

    [Fact]
    public void Hidden_files_and_the_trash_folder_are_filtered()
    {
        FileEntry[] entries = [E(".hidden", false), E("shown.txt", false), E(".Trash", true)];
        Assert.Equal(["shown.txt"], FilesListRules.Visible(entries, false).Select(e => e.Name));
        Assert.Equal([".hidden", "shown.txt"], FilesListRules.Visible(entries, true).Select(e => e.Name));
    }

    [Fact]
    public void The_preview_kind_comes_from_the_media_type_then_the_extension()
    {
        Assert.Equal(FilesPreviewKind.Image, FilesPreviewRules.KindOf("image/png", "x"));
        Assert.Equal(FilesPreviewKind.Pdf, FilesPreviewRules.KindOf("application/pdf", "x"));
        Assert.Equal(FilesPreviewKind.Text, FilesPreviewRules.KindOf("text/plain", "x"));
        Assert.Equal(FilesPreviewKind.Image, FilesPreviewRules.KindOf(null, "photo.PNG"));
        Assert.Equal(FilesPreviewKind.Text, FilesPreviewRules.KindOf("application/octet-stream", "notes.md"));
        Assert.Equal(FilesPreviewKind.Eml, FilesPreviewRules.KindOf(string.Empty, "mail.eml"));
        Assert.Equal(FilesPreviewKind.None, FilesPreviewRules.KindOf("application/zip", "a.zip"));
    }
}
