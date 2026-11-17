using Barbican.Core.Editor;
using Barbican.Presentation.Editor;

// Row labels and ids are compared as ordinal text in these tests; the comparer overloads add nothing here.
#pragma warning disable MA0006, MA0002

namespace Barbican.Presentation.Tests.Editor;

/// <summary>Copy, cut and paste through a fake clipboard (Rust copy/paste; images, files, HTML, plain text, lists).</summary>
public sealed class EditorClipboardActionsTests
{
    private static EditorSession Session(string text = "hello world") => new(new Doc([Block.Para(text)]));

    private static (int Width, int Height)? Size(byte[] _) => (1200, 600);

    [Fact]
    public void Copy_puts_html_and_text_on_the_clipboard_and_cut_removes_the_selection()
    {
        var s = Session();
        var cb = new FakeEditorClipboard();
        s.SelectRange(new Pos(0, 0), new Pos(0, 5));
        Assert.True(EditorClipboardActions.Copy(s, cb, cut: false));
        Assert.Equal("hello", cb.Text);
        Assert.Contains("hello", cb.Html, StringComparison.Ordinal);
        Assert.Equal("hello world", s.Doc.Blocks[0].Text);
        Assert.True(EditorClipboardActions.Copy(s, cb, cut: true));
        Assert.Equal(" world", s.Doc.Blocks[0].Text);
    }

    [Fact]
    public void Copy_with_no_selection_does_nothing()
    {
        var s = Session();
        var cb = new FakeEditorClipboard { Text = "keep" };
        Assert.False(EditorClipboardActions.Copy(s, cb, cut: false));
        Assert.Equal("keep", cb.Text);
    }

    [Fact]
    public void Formatted_text_survives_a_copy_and_paste_round_trip()
    {
        var s = Session("plain bold");
        s.SelectRange(new Pos(0, 6), new Pos(0, 10));
        s.ToggleMark(Mark.Bold);
        var cb = new FakeEditorClipboard();
        s.SelectRange(new Pos(0, 6), new Pos(0, 10));
        EditorClipboardActions.Copy(s, cb, cut: false);
        s.SetCaret(new Pos(0, 0));
        EditorClipboardActions.Paste(s, cb, plainOnly: false);
        Assert.Equal("boldplain bold", s.Doc.Blocks[0].Text);
        Assert.True(s.Doc.Blocks[0].Runs[0].Marks.Bold);
    }

    [Fact]
    public void Fragment_strips_the_cf_html_header()
    {
        Assert.Equal("<b>x</b>", EditorClipboardActions.Fragment("Version:0.9\r\n<html><!--StartFragment--><b>x</b><!--EndFragment--></html>"));
        Assert.Equal("<p>y</p>", EditorClipboardActions.Fragment("Version:0.9\r\nStartHTML:1\r\n<p>y</p>"));
        Assert.Equal("<i>z</i>", EditorClipboardActions.Fragment("<i>z</i>"));
    }

    [Fact]
    public void Paste_takes_html_before_text_and_plain_paste_takes_only_the_text()
    {
        var s = Session("");
        var cb = new FakeEditorClipboard { Html = "<p><strong>rich</strong></p>", Text = "rich" };
        EditorClipboardActions.Paste(s, cb, plainOnly: false);
        Assert.Equal("rich", s.Doc.Blocks[0].Text);
        Assert.True(s.Doc.Blocks[0].Runs[0].Marks.Bold);

        var p = Session("");
        EditorClipboardActions.Paste(p, cb, plainOnly: true);
        Assert.Equal("rich", p.Doc.Blocks[0].Text);
        Assert.False(p.Doc.Blocks[0].Runs[0].Marks.Bold);
    }

    [Fact]
    public void Paste_in_plain_mode_or_a_code_block_ignores_html_and_images()
    {
        var plain = Session("");
        plain.Plain = true;
        var cb = new FakeEditorClipboard { Html = "<b>x</b>", Text = "text", Image = new ClipboardFile("a.png", "image/png", [1, 2, 3]) };
        EditorClipboardActions.Paste(plain, cb, plainOnly: false);
        Assert.Equal("text", plain.Doc.Blocks[0].Text);
        Assert.Empty(plain.InlineImages);

        var code = Session("");
        code.SetBlock(new BlockKind.CodeBlock());
        EditorClipboardActions.Paste(code, cb, plainOnly: false);
        Assert.Equal("text", code.Doc.Blocks[0].Text);
    }

    [Fact]
    public void Pasted_image_files_become_inline_images_with_a_cid_and_a_capped_width()
    {
        var s = Session("x");
        var cb = new FakeEditorClipboard
        {
            Files = [new ClipboardFile("shot.png", "image/png", [9, 9]), new ClipboardFile("notes.txt", "text/plain", [1]), new ClipboardFile("draw.svg", "image/svg+xml", [2])],
            Html = "<p>ignored</p>",
        };
        EditorClipboardActions.Paste(s, cb, plainOnly: false, Size);
        var images = s.InlineImages;
        Assert.Single(images);
        Assert.Equal("shot.png", images[0].Name);
        Assert.Equal("shot.png", images[0].Alt);
        Assert.Matches("^img[0-9a-f]{16}@barbican$", images[0].Cid);
        Assert.Equal((600, 300), (images[0].Width, images[0].Height));
        Assert.Null(images[0].BlobId);
    }

    [Fact]
    public void A_bitmap_on_the_clipboard_is_inserted_when_there_is_no_html()
    {
        var s = Session("x");
        EditorClipboardActions.Paste(s, new FakeEditorClipboard { Image = new ClipboardFile("pasted-image.png", "image/png", [1]) }, false, _ => (100, 40));
        Assert.Single(s.InlineImages);
        Assert.Equal((0, 0), (s.InlineImages[0].Width, s.InlineImages[0].Height));
    }

    [Fact]
    public void Plain_text_with_list_markers_pastes_as_a_list_and_one_line_stays_inline()
    {
        var s = Session("ab");
        s.SetCaret(new Pos(0, 1));
        EditorClipboardActions.Paste(s, new FakeEditorClipboard { Text = "X" }, false);
        Assert.Equal("aXb", s.Doc.Blocks[0].Text);

        var l = Session("");
        EditorClipboardActions.Paste(l, new FakeEditorClipboard { Text = "- one\r\n- two" }, false);
        Assert.Contains(l.Doc.Blocks, b => b.Kind is BlockKind.Bullet && b.Text == "two");

        var t = Session("");
        EditorClipboardActions.Paste(t, new FakeEditorClipboard { Text = "- one\n- two" }, plainOnly: true);
        Assert.DoesNotContain(t.Doc.Blocks, b => b.Kind.IsList);
    }

    [Theory]
    [InlineData("image/png", true)]
    [InlineData("image/jpeg", true)]
    [InlineData("image/svg+xml", false)]
    [InlineData("text/plain", false)]
    public void Only_raster_pictures_go_inline(string mime, bool expected)
    {
        Assert.Equal(expected, EditorClipboardActions.IsPictureMime(mime));
    }
}
