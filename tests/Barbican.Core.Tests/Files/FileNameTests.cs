// Not Rust rows: the pure naming rules of the Files app (G-019): Bulwark lib/file-name-rules.ts acceptedFileName, lib/download-filename.ts (the filename rules of settings), lib/files-upload-conflicts.ts getUniqueName, lib/files-zip.ts;
// and what a hostile node name may do on a disk or in an archive (D-M5-03). M5 stream files-data.

#pragma warning disable MA0006, MA0002 // test assertions compare ordinal string literals

using Barbican.Core.Files;

namespace Barbican.Core.Tests.Files;

public sealed class FileNameTests
{
    private static readonly FileNameRules Rules = new("/\\:*", ["con", "Aux"], 12);

    [Fact]
    public void A_local_name_is_made_acceptable_to_the_server()
    {
        Assert.Equal("a_b_c.txt", FileNameChecks.AcceptedName("a:b*c.txt", Rules));
        Assert.Equal("a_b.txt", FileNameChecks.AcceptedName("a\u0007b.txt", Rules));
        Assert.Equal("con_.txt", FileNameChecks.AcceptedName("con.txt", Rules));
        Assert.Equal("AUX_", FileNameChecks.AcceptedName("AUX", Rules));
        Assert.Equal("report.pdf", FileNameChecks.AcceptedName("report.pdf", Rules));
        Assert.Equal("anything:goes", FileNameChecks.AcceptedName("anything:goes", null));

        // over the limit: shortened by whole characters in UTF-8 bytes, the extension kept
        var cut = FileNameChecks.AcceptedName("abcdefghijklmnop.txt", Rules);
        Assert.Equal("abcdefgh.txt", cut);
        Assert.Equal(12, System.Text.Encoding.UTF8.GetByteCount(FileNameChecks.AcceptedName("ééééééé.txt", Rules)));
        Assert.Null(FileNameChecks.ProblemOf(FileNameChecks.AcceptedName("con:x*y/z\\w.TXT", Rules), Rules));
    }

    [Fact]
    public void A_problem_has_words_for_the_dialog()
    {
        Assert.Contains("forbidden", FileNameChecks.Message(new FileNameProblem("chars", ": *")), StringComparison.Ordinal);
        Assert.Contains("control", FileNameChecks.Message(new FileNameProblem("control", string.Empty)), StringComparison.Ordinal);
        Assert.Contains("too long", FileNameChecks.Message(new FileNameProblem("tooLong", string.Empty)), StringComparison.Ordinal);
        Assert.Contains("reserved", FileNameChecks.Message(new FileNameProblem("reserved", string.Empty)), StringComparison.Ordinal);
    }

    [Fact]
    public void Unique_names_count_up_and_a_leading_dot_is_not_an_extension()
    {
        var used = new HashSet<string>(StringComparer.Ordinal) { ".bashrc", "a.tar.gz", "a.tar (2).gz", "dir" };
        Assert.Equal(".bashrc (2)", FileNames.UniqueName(".bashrc", used));
        Assert.Equal("a.tar (3).gz", FileNames.UniqueName("a.tar.gz", used));
        Assert.Equal("dir (2)", FileNames.UniqueName("dir", used));
        Assert.Equal("notes (copy).txt", FileNames.CopyName("notes.txt", false));
        Assert.Equal("Docs (copy)", FileNames.CopyName("Docs", true));
        Assert.Equal("v1.2 (copy)", FileNames.CopyName("v1.2", true));
        Assert.Equal(".bashrc (copy)", FileNames.CopyName(".bashrc", false));
    }

    [Theory]
    [InlineData("My Report.PDF", "keep", false, false, true, "My Report.PDF")]
    [InlineData("My   Report.PDF", "underscore", true, false, true, "my_report.pdf")]
    [InlineData("My Report.PDF", "dash", false, false, true, "My-Report.PDF")]
    [InlineData("Ärger über ß.txt", "keep", false, true, true, "Arger uber ß.txt")]
    [InlineData("a__b--c.txt", "keep", false, false, true, "a_b-c.txt")]
    [InlineData("a__b--c.txt", "keep", false, false, false, "a_b--c.txt")]
    [InlineData("a<b>c:d.txt", "keep", false, false, true, "a_b_c_d.txt")]
    [InlineData("   ", "keep", false, false, true, "attachment")]
    public void Download_names_follow_the_filename_rules_of_settings(string name, string spaces, bool lower, bool strip, bool collapse, string expected) =>
        Assert.Equal(expected, FileNames.DownloadName(name, spaces, lower, strip, collapse));

    [Theory]
    [InlineData("..\\..\\evil.txt", ".._.._evil.txt")]
    [InlineData("../x", ".._x")]
    [InlineData("CON", "_CON")]
    [InlineData("nul.txt", "_nul.txt")]
    [InlineData("COM1.log", "_COM1.log")]
    [InlineData("a:b?.txt", "a_b_.txt")]
    [InlineData("trailing. . ", "trailing")]
    [InlineData("", "file")]
    [InlineData("..", "file")]
    [InlineData("‮gnp.exe", "_gnp.exe")]
    public void A_local_name_is_one_safe_path_segment(string name, string expected) => Assert.Equal(expected, FileNames.LocalName(name));

    [Fact]
    public void A_long_local_name_keeps_its_extension_within_255_units()
    {
        var name = FileNames.LocalName(new string('x', 400) + ".pdf");
        Assert.Equal(255, name.Length);
        Assert.EndsWith(".pdf", name, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("..", "_")]
    [InlineData(".", "_")]
    [InlineData("", "_")]
    [InlineData("a/b\\c", "a_b_c")]
    [InlineData("ok.txt", "ok.txt")]
    public void A_zip_segment_cannot_climb_out(string name, string expected) => Assert.Equal(expected, FileNames.ZipSegment(name));

    [Fact]
    public void Media_types_come_from_the_extension()
    {
        Assert.Equal("application/pdf", FileMimeTypes.For("a.PDF"));
        Assert.Equal("image/jpeg", FileMimeTypes.For("x.jpeg"));
        Assert.Equal("application/octet-stream", FileMimeTypes.For("noext"));
        Assert.Equal("application/octet-stream", FileMimeTypes.For("a.unknown"));
    }

    [Fact]
    public void A_path_is_built_from_the_parents_and_a_loop_ends()
    {
        FileEntry[] nodes = [FileTestData.N("a", null, "Docs", true), FileTestData.N("b", "a", "Invoices", true), FileTestData.N("x", "y", "Loop", true), FileTestData.N("y", "x", "Loop2", true)];
        Assert.Equal("/", FilePaths.Of(nodes, null));
        Assert.Equal("/Docs/Invoices", FilePaths.Of(nodes, "b"));
        Assert.Equal(string.Empty, FilePaths.Of(nodes, "missing"));
        Assert.Equal(string.Empty, FilePaths.Of(nodes, "x"));
    }

    [Fact]
    public void A_shared_node_key_carries_its_account_and_a_key_splits_back()
    {
        Assert.Equal("n1", FileEntry.MakeKey("A1", "n1", false));
        Assert.Equal("A1|n1", FileEntry.MakeKey("A1", "n1", true));
        Assert.Equal(("A1", "n1"), FileEntry.SplitKey("A1|n1", "primary"));
        Assert.Equal(("primary", "n1"), FileEntry.SplitKey("n1", "primary"));
    }

    [Fact]
    public void A_trash_listing_survives_a_looping_tree()
    {
        FileEntry[] nodes = [FileTestData.N("t", null, ".Trash", true), FileTestData.N("a", "b", "A", true), FileTestData.N("b", "a", "B", true), FileTestData.N("c", "t", "C", true)];
        var sub = FileTrash.Subtree(nodes);
        Assert.Equal(2, sub.Count);
        Assert.Contains("c", sub);
    }
}
