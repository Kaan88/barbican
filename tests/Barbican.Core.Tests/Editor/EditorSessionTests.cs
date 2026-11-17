#pragma warning disable MA0002, MA0006 // asserts compare strings ordinally by nature (the ported Rust asserts)
using Barbican.Core.Editor;

namespace Barbican.Core.Tests.Editor;

/// <summary>The 10 tests of Rust src/rte/editor.rs, ported one to one (M3 stream editor-core).</summary>
public sealed class EditorSessionTests
{
    private static EditorSession Ed(string text)
    {
        var e = new EditorSession(Doc.FromText(text));
        e.SetCaret(e.Doc.End);
        return e;
    }

    private static List<string> Texts(EditorSession e) => [.. e.Doc.Blocks.Select(b => b.Text)];

    private static void Typ(EditorSession e, string s)
    {
        foreach (var c in s)
        {
            e.InsertText(c.ToString());
        }
    }

    [Fact]
    [RustTest("rte::editor::typing_and_undo_groups")]
    public void Typing_and_undo_groups()
    {
        var e = Ed("");
        Typ(e, "hello");
        e.Enter(false);
        Typ(e, "world");
        Assert.Equal(["hello", "world"], Texts(e));
        Assert.True(e.Undo());
        Assert.Equal(["hello", ""], Texts(e));
        Assert.True(e.Undo());
        Assert.Equal(["hello"], Texts(e));
        Assert.True(e.Undo());
        Assert.Equal([""], Texts(e));
        Assert.True(e.Redo());
        Assert.True(e.Redo());
        Assert.Equal(["hello", ""], Texts(e));
    }

    [Fact]
    [RustTest("rte::editor::markdown_block_rules")]
    public void Markdown_block_rules()
    {
        var e = Ed("");
        Typ(e, "# Title");
        Assert.Equal(new BlockKind.Heading(1), e.Doc.Blocks[0].Kind);
        Assert.Equal(["Title"], Texts(e));
        e.Enter(false);
        Typ(e, "- a");
        Assert.Equal(new BlockKind.Bullet(), e.Doc.Blocks[1].Kind);
        e.Enter(false);
        Assert.Equal(new BlockKind.Bullet(), e.Doc.Blocks[2].Kind);
        e.Enter(false); // an empty item leaves the list
        Assert.Equal(new BlockKind.Para(), e.Doc.Blocks[2].Kind);
        Typ(e, "1. x");
        Assert.Equal(new BlockKind.Ordered(), e.Doc.Blocks[2].Kind);
        e.Undo();
        e.Undo();
        Assert.Equal(new BlockKind.Para(), e.Doc.Blocks[2].Kind);
        Assert.Equal("1. ", e.Doc.Blocks[2].Text);
    }

    [Fact]
    [RustTest("rte::editor::markdown_inline_rules")]
    public void Markdown_inline_rules()
    {
        var e = Ed("");
        Typ(e, "a **bold** and *it* `c`");
        var b = e.Doc.Blocks[0];
        Assert.Equal("a bold and it c", b.Text);
        Assert.Contains(b.Runs, r => r.Text == "bold" && r.Marks.Bold);
        Assert.Contains(b.Runs, r => r.Text == "it" && r.Marks.Italic && !r.Marks.Bold);
        Assert.Contains(b.Runs, r => r.Text == "c" && r.Marks.Code);
        Assert.Contains(b.Runs, r => r.Text == " and " && r.Marks.IsPlain);
    }

    [Fact]
    [RustTest("rte::editor::toggle_marks_selection_and_caret")]
    public void Toggle_marks_selection_and_caret()
    {
        var e = Ed("hello world");
        e.SelectRange(new Pos(0, 0), new Pos(0, 5));
        e.ToggleMark(Mark.Bold);
        Assert.True(e.Doc.Blocks[0].Runs[0].Marks.Bold);
        Assert.True(e.CurrentMarks().Bold);
        e.ToggleMark(Mark.Bold);
        Assert.Single(e.Doc.Blocks[0].Runs);
        e.SetCaret(new Pos(0, 11));
        e.ToggleMark(Mark.Italic);
        Typ(e, "!");
        Assert.True(e.Doc.Blocks[0].Runs[^1].Marks.Italic);
    }

    [Fact]
    [RustTest("rte::editor::lists_indent_and_backspace")]
    public void Lists_indent_and_backspace()
    {
        var e = Ed("");
        Typ(e, "- a");
        e.Enter(false);
        Typ(e, "b");
        Assert.True(e.Indent(1));
        Assert.Equal(1, e.Doc.Blocks[1].Indent);
        e.SetCaret(new Pos(1, 0));
        e.Backspace(false);
        Assert.Equal(0, e.Doc.Blocks[1].Indent);
        e.Backspace(false);
        Assert.Equal(new BlockKind.Para(), e.Doc.Blocks[1].Kind);
        e.Backspace(false);
        Assert.Equal(["ab"], Texts(e));
    }

    [Fact]
    [RustTest("rte::editor::links")]
    public void Links()
    {
        var e = Ed("see site");
        e.SelectRange(new Pos(0, 4), new Pos(0, 8));
        e.SetLink("example.com", "");
        Assert.Equal("https://example.com", e.Doc.Blocks[0].Runs[1].Marks.Link);
        e.SetCaret(new Pos(0, 6));
        Assert.Equal("https://example.com", e.CurrentLink());
        e.SetLink("", "");
        Assert.All(e.Doc.Blocks[0].Runs, r => Assert.Null(r.Marks.Link));
    }

    [Fact]
    [RustTest("rte::editor::tables")]
    public void Tables()
    {
        var e = Ed("");
        e.InsertTable(2, 2);
        Assert.Equal(4, e.Doc.Blocks.Count(b => b.Kind.IsCell));
        Typ(e, "a");
        Assert.True(e.CellTab(false));
        Typ(e, "b");
        e.ApplyTable(TableOp.ColRight);
        Assert.Equal(6, e.Doc.Blocks.Count(b => b.Kind.IsCell));
        e.ApplyTable(TableOp.DelRow);
        Assert.Equal(3, e.Doc.Blocks.Count(b => b.Kind.IsCell));
        e.ApplyTable(TableOp.DelTable);
        Assert.All(e.Doc.Blocks, b => Assert.False(b.Kind.IsCell));
    }

    [Fact]
    [RustTest("rte::editor::image_insert_keeps_following_text")]
    public void Image_insert_keeps_following_text()
    {
        var e = Ed("");
        e.Doc.Blocks.Add(Block.Para("On Mon, X wrote:"));
        e.SetCaret(new Pos(0, 0));
        e.InsertImage(new ImageData { Cid = "a@b" });
        Typ(e, "hi");
        Assert.IsType<BlockKind.Image>(e.Doc.Blocks[0].Kind);
        Assert.Equal(["hi", "On Mon, X wrote:"], Texts(e).Skip(1).ToList());
        e.SetCaret(new Pos(1, 0));
        e.Backspace(false);
        Assert.DoesNotContain(e.Doc.Blocks, b => b.Kind is BlockKind.Image);
    }

    [Fact]
    [RustTest("rte::editor::replace_keeps_quote")]
    public void Replace_keeps_quote()
    {
        var e = Ed("draft");
        e.Doc.Blocks.Add(Block.Para("On Mon, X wrote:"));
        e.Doc.Blocks.Add(new Block(new BlockKind.Quoted("<p>q</p>", "q", Open: false, Cite: true)));
        e.Doc.Fix();
        e.ReplaceText("better draft", true);
        Assert.Equal("better draft", e.Doc.Blocks[0].Text);
        Assert.Contains(e.Doc.Blocks, b => b.Kind is BlockKind.Quoted);
        Assert.Contains(e.Doc.Blocks, b => b.Text == "On Mon, X wrote:");
    }

    [Fact]
    [RustTest("rte::editor::image_resize_and_replace_range")]
    public void Image_resize_and_replace_range()
    {
        var e = Ed("ab");
        e.InsertImage(new ImageData { Src = "https://x.org/a.png" });
        var b = e.Doc.Blocks.ToList().FindIndex(x => x.Kind is BlockKind.Image);
        Assert.True(e.ResizeImage(b, 200, 100, true));
        Assert.True(e.ResizeImage(b, 220, 110, false));
        Assert.True(e.Doc.Blocks[b].Kind is BlockKind.Image { Data: { Width: 220, Height: 110 } });
        e.Undo();
        Assert.True(e.Doc.Blocks[b].Kind is BlockKind.Image { Data.Width: 0 }, "one undo step per drag");
        Assert.False(e.ResizeImage(0, 1, 1, true), "not an image");

        e = Ed("I wrte this");
        e.ReplaceRange(new Pos(0, 2), new Pos(0, 6), "write");
        Assert.Equal(["I write this"], Texts(e));
    }
}
