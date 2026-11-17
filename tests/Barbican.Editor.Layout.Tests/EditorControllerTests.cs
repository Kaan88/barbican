using Barbican.Core.Editor;
using Barbican.Editor.Layout.Input;
using Microsoft.Extensions.Time.Testing;

namespace Barbican.Editor.Layout.Tests;

/// <summary>Caret, selection and pointer behaviour of the controller on a real session and layout (Rust rte/mod.rs handle_key and on_pointer).</summary>
public sealed class EditorControllerTests
{
    private static (EditorController C, EditorSession S, FakeTimeProvider T) Make(Doc doc, float width = 300f)
    {
        var t = new FakeTimeProvider();
        var s = new EditorSession(doc, t);
        var layout = new DocLayout(new RichTextKitEngine(), TestFonts.Style);
        var c = new EditorController(s, layout, t);
        c.Build(width);
        return (c, s, t);
    }

    private static Doc Doc2() => new([Block.Para("hello brave new world"), Block.Para("second line")]);

    [Fact]
    public void Typing_inserts_at_the_caret_and_drops_control_characters()
    {
        var (c, s, _) = Make(Doc2());
        s.SetCaret(new Pos(0, 5));
        Assert.True(c.TypeText("X\u0001Y"));
        c.Build(300f);
        Assert.Equal("helloXY brave new world", s.Doc.Blocks[0].Text);
        Assert.Equal(new Pos(0, 7), s.Sel.Focus);
        Assert.False(c.TypeText("\u0001"));
    }

    [Fact]
    public void Arrows_step_by_grapheme_cross_blocks_and_collapse_a_selection_to_its_edge()
    {
        var (c, s, _) = Make(new Doc([Block.Para("a\U0001F600b"), Block.Para("z")]));
        s.SetCaret(new Pos(0, 1));
        c.Key(NavKey.Right, false, false, false);
        Assert.Equal(new Pos(0, 3), s.Sel.Focus);
        c.Key(NavKey.Right, false, false, false);
        c.Key(NavKey.Right, false, false, false);
        Assert.Equal(new Pos(1, 0), s.Sel.Focus);
        c.Key(NavKey.Left, false, false, false);
        Assert.Equal(new Pos(0, 4), s.Sel.Focus);
        s.SelectRange(new Pos(0, 1), new Pos(0, 3));
        c.Key(NavKey.Left, false, false, false);
        Assert.Equal(new Pos(0, 1), s.Sel.Focus);
        s.SelectRange(new Pos(0, 1), new Pos(0, 3));
        c.Key(NavKey.Right, false, false, false);
        Assert.Equal(new Pos(0, 3), s.Sel.Focus);
    }

    [Fact]
    public void Shift_arrows_extend_from_the_anchor_and_ctrl_arrows_move_by_word()
    {
        var (c, s, _) = Make(Doc2());
        s.SetCaret(new Pos(0, 0));
        c.Key(NavKey.Right, true, false, false);
        Assert.Equal(new Pos(0, 6), s.Sel.Focus);
        c.Key(NavKey.Right, true, true, false);
        Assert.Equal(new Pos(0, 6), s.Sel.Anchor);
        Assert.Equal(new Pos(0, 12), s.Sel.Focus);
        c.Key(NavKey.Left, true, false, false);
        Assert.Equal(new Pos(0, 6), s.Sel.Focus);
        Assert.Equal(16, EditorController.NextWord("hello brave new world", 12));
        Assert.Equal(0, EditorController.PrevWord("hello", 3));
    }

    [Fact]
    public void Home_and_end_with_ctrl_go_to_the_document_edges_and_without_to_the_line_edges()
    {
        var (c, s, _) = Make(Doc2(), 120f);
        s.SetCaret(new Pos(0, 8));
        c.Key(NavKey.Home, false, false, false);
        Assert.True(s.Sel.Focus.O < 8);
        c.Key(NavKey.End, false, false, false);
        Assert.True(s.Sel.Focus.O >= 8);
        c.Key(NavKey.End, true, false, false);
        Assert.Equal(s.Doc.End, s.Sel.Focus);
        c.Key(NavKey.Home, true, false, false);
        Assert.Equal(new Pos(0, 0), s.Sel.Focus);
    }

    [Fact]
    public void Up_and_down_keep_the_goal_column_across_short_lines()
    {
        var (c, s, _) = Make(new Doc([Block.Para("a long first line here"), Block.Para("ab"), Block.Para("another long line here")]));
        s.SetCaret(new Pos(0, 12));
        c.Key(NavKey.Down, false, false, false);
        Assert.Equal(1, s.Sel.Focus.B);
        Assert.Equal(2, s.Sel.Focus.O);
        c.Key(NavKey.Down, false, false, false);
        Assert.Equal(2, s.Sel.Focus.B);
        Assert.InRange(s.Sel.Focus.O, 10, 14);
        c.Key(NavKey.Up, false, true, false);
        Assert.False(s.Sel.IsCollapsed);
    }

    [Fact]
    public void Enter_backspace_delete_and_tab_edit_the_document()
    {
        var (c, s, _) = Make(Doc2());
        s.SetCaret(new Pos(0, 5));
        c.Key(NavKey.Enter, false, false, false);
        Assert.Equal(3, s.Doc.Blocks.Count);
        c.Key(NavKey.Backspace, false, false, false);
        Assert.Equal(2, s.Doc.Blocks.Count);
        c.Key(NavKey.Delete, false, false, false);
        Assert.Equal("hellobrave new world", s.Doc.Blocks[0].Text);
        Assert.False(c.Key(NavKey.Enter, true, false, false));
        c.Key(NavKey.Tab, false, false, false);
        Assert.Contains('\t', s.Doc.Blocks[0].Text);
    }

    [Fact]
    public void Tab_in_a_list_indents_and_in_a_cell_moves_to_the_next_cell()
    {
        var list = new Doc([Block.WithText(new BlockKind.Bullet(), "one", Marks.Plain), Block.WithText(new BlockKind.Bullet(), "two", Marks.Plain)]);
        var (c, s, _) = Make(list);
        s.SetCaret(new Pos(1, 1));
        c.Key(NavKey.Tab, false, false, false);
        Assert.Equal(1, s.Doc.Blocks[1].Indent);
        c.Key(NavKey.Tab, false, true, false);
        Assert.Equal(0, s.Doc.Blocks[1].Indent);

        var (c2, s2, _) = Make(new Doc([Block.Para("x")]));
        s2.InsertTable(2, 2);
        c2.Build(300f);
        var before = s2.Sel.Focus.B;
        c2.Key(NavKey.Tab, false, false, false);
        Assert.Equal(before + 1, s2.Sel.Focus.B);
    }

    [Fact]
    public void Escape_clears_a_selection_first_and_a_second_escape_is_left_to_the_host()
    {
        var (c, s, _) = Make(Doc2());
        s.SelectRange(new Pos(0, 0), new Pos(0, 5));
        Assert.True(c.Key(NavKey.Escape, false, false, false));
        Assert.True(s.Sel.IsCollapsed);
        Assert.False(c.Key(NavKey.Escape, false, false, false));
    }

    [Fact]
    public void Click_double_click_and_triple_click_select_caret_word_and_block()
    {
        var (c, s, t) = Make(Doc2());
        var g = c.Layout.Blocks[0];
        var x = c.Layout.Caret(new Pos(0, 8), false).X;
        var y = g.TextY + 5f;
        c.PointerDown(x, y, shift: false);
        Assert.True(s.Sel.IsCollapsed);
        t.Advance(TimeSpan.FromMilliseconds(100));
        c.PointerDown(x, y, shift: false);
        Assert.Equal("brave", s.SelectedText());
        t.Advance(TimeSpan.FromMilliseconds(100));
        c.PointerDown(x, y, shift: false);
        Assert.Equal("hello brave new world", s.SelectedText());
        t.Advance(TimeSpan.FromMilliseconds(900));
        c.PointerDown(x, y, shift: false);
        Assert.True(s.Sel.IsCollapsed);
    }

    [Fact]
    public void Drag_extends_the_selection_and_shift_click_extends_from_the_caret()
    {
        var (c, s, t) = Make(Doc2());
        var g0 = c.Layout.Blocks[0];
        var g1 = c.Layout.Blocks[1];
        c.PointerDown(c.Layout.Caret(new Pos(0, 2), false).X, g0.TextY + 5f, false);
        Assert.True(c.Dragging);
        c.PointerDrag(c.Layout.Caret(new Pos(1, 4), false).X, g1.TextY + 5f);
        Assert.Equal(new Pos(0, 2), s.Sel.Anchor);
        Assert.Equal(1, s.Sel.Focus.B);
        c.PointerUp();
        Assert.False(c.Dragging);
        t.Advance(TimeSpan.FromSeconds(2));
        s.SetCaret(new Pos(0, 1));
        c.PointerDown(c.Layout.Caret(new Pos(0, 9), false).X, g0.TextY + 5f, shift: true);
        Assert.Equal(new Pos(0, 1), s.Sel.Anchor);
        Assert.False(s.Sel.IsCollapsed);
    }

    [Fact]
    public void Double_click_drag_extends_by_whole_words()
    {
        var (c, s, t) = Make(Doc2());
        var g0 = c.Layout.Blocks[0];
        var y = g0.TextY + 5f;
        var x = c.Layout.Caret(new Pos(0, 8), false).X;
        c.PointerDown(x, y, false);
        t.Advance(TimeSpan.FromMilliseconds(50));
        c.PointerDown(x, y, false);
        c.PointerDrag(c.Layout.Caret(new Pos(0, 14), false).X, y);
        Assert.Equal("brave new", s.SelectedText());
    }

    [Fact]
    public void Clicking_the_quote_header_toggles_the_quoted_original()
    {
        var doc = new Doc([Block.Para("reply"), new Block(new BlockKind.Quoted("<p>x</p>", "quoted text", false, true))]);
        var (c, s, _) = Make(doc);
        var h = c.Layout.Blocks[1].Header!.Value;
        c.PointerDown(h.X + 20f, h.Y + 5f, false);
        c.Build(300f);
        Assert.True(((BlockKind.Quoted)s.Doc.Blocks[1].Kind).Open);
    }

    [Fact]
    public void Right_click_keeps_a_selection_it_lands_in_and_moves_the_caret_otherwise()
    {
        var (c, s, _) = Make(Doc2());
        s.SelectRange(new Pos(0, 0), new Pos(0, 11));
        var g = c.Layout.Blocks[0];
        c.RightDown(c.Layout.Caret(new Pos(0, 4), false).X, g.TextY + 5f);
        Assert.False(s.Sel.IsCollapsed);
        c.RightDown(c.Layout.Caret(new Pos(0, 18), false).X, g.TextY + 5f);
        Assert.True(s.Sel.IsCollapsed);
        Assert.InRange(s.Sel.Focus.O, 16, 20);
    }

    [Fact]
    public void Image_resize_drag_keeps_the_ratio_clamps_and_is_one_undo_step()
    {
        var img = new ImageData { Src = "https://example.test/a.png", Width = 200, Height = 100 };
        var (c, s, _) = Make(new Doc([Block.Para("a"), new Block(new BlockKind.Image(img)), Block.Para("b")]), 400f);
        s.SetCaret(new Pos(1, 0));
        Assert.True(c.ResizeBegin(leftCorner: false, 200f));
        Assert.True(c.ResizeMove(260f));
        c.Build(400f);
        c.ResizeMove(300f);
        c.Build(400f);
        c.ResizeEnd();
        var data = ((BlockKind.Image)s.Doc.Blocks[1].Kind).Data;
        Assert.Equal((300, 150), (data.Width, data.Height));
        Assert.True(s.Undo());
        Assert.Equal(200, ((BlockKind.Image)s.Doc.Blocks[1].Kind).Data.Width);
        Assert.Equal(24f, EditorController.NewWidth(200f, -500f, 400f));
        Assert.Equal(400f, EditorController.NewWidth(200f, 900f, 400f));
        s.SetCaret(new Pos(0, 0));
        Assert.False(c.ResizeBegin(false, 0f));
    }
}
