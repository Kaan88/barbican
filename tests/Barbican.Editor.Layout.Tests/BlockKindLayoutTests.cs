using Barbican.Core.Editor;
using Barbican.Core.Editor.Spell;
using SkiaSharp;

namespace Barbican.Editor.Layout.Tests;

/// <summary>M3 step 1: geometry, caret, hit test and paint of every block kind (Rust layout.rs build/hit/caret_rect/move_vertical).</summary>
public sealed class BlockKindLayoutTests
{
    private static DocLayout Layout(Doc d, float width = 400f)
    {
        var l = new DocLayout(new RichTextKitEngine(), TestFonts.Style);
        l.Build(d, width);
        return l;
    }

    private static Doc Table(int rows, int cols)
    {
        var blocks = new List<Block>();
        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < cols; c++)
            {
                blocks.Add(Block.WithText(new BlockKind.Cell(0, r, c, r == 0), $"r{r}c{c}", Marks.Plain));
            }
        }

        return new Doc(blocks);
    }

    private static byte[] Png(int w, int h)
    {
        using var bmp = new SKBitmap(w, h);
        bmp.Erase(SKColors.Red);
        using var data = bmp.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Fact]
    public void Table_cells_share_row_heights_and_split_the_width_in_equal_columns()
    {
        var d = Table(2, 3);
        var l = Layout(d, 300f);
        var g = l.Blocks;
        Assert.Equal(6, g.Count);
        Assert.Equal(100f, g[1].X);
        Assert.Equal(100f, g[1].Width);
        Assert.Equal(g[0].Y, g[2].Y);
        Assert.Equal(g[0].Height, g[2].Height);
        Assert.Equal(g[0].Y + g[0].Height, g[3].Y, 0.01);
        Assert.Equal(g[1].X + 10f, g[1].TextX);
        Assert.Equal(g[3].Y + 6f, g[3].TextY, 0.01);
        Assert.Equal(g[^1].Y + g[^1].Height, l.Height, 0.01);
    }

    [Fact]
    public void Table_hit_test_picks_the_column_under_the_point()
    {
        var d = Table(2, 3);
        var l = Layout(d, 300f);
        var g = l.Blocks;
        var (p, _) = l.HitTest(g[4].X + 30f, g[4].Y + 10f);
        Assert.Equal(4, p.B);
        var (q, _) = l.HitTest(g[2].X + 5f, g[2].Y + 10f);
        Assert.Equal(2, q.B);
    }

    [Fact]
    public void Vertical_movement_in_a_table_goes_by_row_and_enters_the_goal_column()
    {
        var d = new Doc([Block.Para("before"), .. Table(2, 2).Blocks, Block.Para("after")]);
        d.Fix();
        var l = Layout(d, 300f);
        var cell = l.Blocks[2];
        var (down, _) = l.MoveVertical(d, new Pos(2, 1), false, 1, cell.X + 20f);
        Assert.Equal(4, down.B);
        var (fromAbove, _) = l.MoveVertical(d, new Pos(0, 2), false, 1, 200f);
        Assert.Equal(2, fromAbove.B);
        var (out1, _) = l.MoveVertical(d, new Pos(4, 0), false, 1, 20f);
        Assert.Equal(5, out1.B);
    }

    [Fact]
    public void Image_uses_its_natural_size_then_its_set_width_and_is_capped_by_the_editor_width()
    {
        var bytes = Png(100, 50);
        var d = new Doc([new Block(new BlockKind.Image(new ImageData { Bytes = bytes }))]);
        var r = Layout(d).ImageRect(0)!.Value;
        Assert.Equal((100f, 50f), (r.Width, r.Height));

        d = new Doc([new Block(new BlockKind.Image(new ImageData { Bytes = bytes, Width = 200 }))]);
        r = Layout(d).ImageRect(0)!.Value;
        Assert.Equal((200f, 100f), (r.Width, r.Height));

        d = new Doc([new Block(new BlockKind.Image(new ImageData { Bytes = bytes, Width = 800, Height = 400 }))]);
        r = Layout(d, 400f).ImageRect(0)!.Value;
        Assert.Equal((400f, 200f), (r.Width, r.Height));
    }

    [Fact]
    public void Image_without_bytes_is_a_placeholder_and_alignment_moves_it()
    {
        var d = new Doc([new Block(new BlockKind.Image(new ImageData { Src = "https://example.test/x.png" })) { Align = Align.Right }]);
        var l = Layout(d, 400f);
        var r = l.ImageRect(0)!.Value;
        Assert.Equal((220f, 56f), (r.Width, r.Height));
        Assert.Equal(180f, r.X);
        var c = l.Caret(new Pos(0, 0), false);
        Assert.Equal(r.X - 3f, c.X);
        Assert.Equal(56f, c.Height);
    }

    [Fact]
    public void Image_paints_its_pixels_and_an_outline_when_selected()
    {
        var d = new Doc([new Block(new BlockKind.Image(new ImageData { Bytes = Png(40, 20) }))]);
        var l = Layout(d);
        using var bmp = new SKBitmap(100, 40);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.White);
            l.Paint(canvas, d, Sel.Caret(new Pos(0, 0)), new PaintOptions { SelectedImage = 0 });
        }

        Assert.Equal(255, bmp.GetPixel(20, 10).Red);
        Assert.Equal(0, bmp.GetPixel(20, 10).Green);
        Assert.NotEqual(SKColors.White, bmp.GetPixel(20, 0));
    }

    [Fact]
    public void Rule_is_nine_pixels_with_sixteen_pixel_margins()
    {
        var d = new Doc([Block.Para("a"), new Block(new BlockKind.Rule()), Block.Para("b")]);
        var g = Layout(d).Blocks;
        Assert.Equal(9f, g[1].Height);
        Assert.Equal(g[0].Y + g[0].Height + 16f, g[1].Y, 0.01);
        Assert.Equal(g[1].Y + 9f + 16f, g[2].Y, 0.01);
    }

    [Fact]
    public void Collapsed_quote_is_a_header_strip_and_opening_it_adds_the_text()
    {
        var closed = new Doc([Block.Para("reply"), new Block(new BlockKind.Quoted("<p>old</p>", "old text old text", false, true))]);
        var l = Layout(closed);
        Assert.Equal(26f, l.Blocks[1].Height);
        Assert.Equal(1, l.QuotedHeaderAt(30f, l.Blocks[1].Y + 5f));
        Assert.Equal(-1, l.QuotedHeaderAt(300f, l.Blocks[1].Y + 5f));

        var open = new Doc([Block.Para("reply"), new Block(new BlockKind.Quoted("<p>old</p>", "old text old text", true, true))]);
        var o = Layout(open);
        Assert.True(o.Blocks[1].Height > 26f + 6f);
        Assert.Equal(14f, o.Blocks[1].TextX);
        Assert.NotNull(o.Blocks[1].Text);
    }

    [Fact]
    public void Quote_and_code_blocks_pad_their_runs_and_stack_without_gaps()
    {
        var d = new Doc(
        [
            Block.WithText(new BlockKind.Quote(), "q1", Marks.Plain),
            Block.WithText(new BlockKind.Quote(), "q2", Marks.Plain),
            Block.WithText(new BlockKind.CodeBlock(), "c1", Marks.Plain),
            Block.WithText(new BlockKind.CodeBlock(), "c2", Marks.Plain),
        ]);
        var g = Layout(d).Blocks;
        Assert.Equal(19f, g[0].TextX);
        Assert.Equal(g[0].Y + 2f, g[0].TextY, 0.01);
        Assert.Equal(g[0].Y + g[0].Height, g[1].Y, 0.01);
        Assert.Equal(g[2].Y + 12f, g[2].TextY, 0.01);
        Assert.Equal(g[2].Y + g[2].Height, g[3].Y, 0.01);
        Assert.Equal(g[3].Y, g[3].TextY, 0.01);
    }

    [Fact]
    public void List_markers_are_painted_left_of_the_item_text_and_number_by_level()
    {
        Assert.Equal("a", DocLayout.Alpha(1));
        Assert.Equal("ab", DocLayout.Alpha(28));
        Assert.Equal("xiv", DocLayout.Roman(14));
        var d = new Doc(
        [
            Block.WithText(new BlockKind.Ordered(), "one", Marks.Plain),
            Block.WithText(new BlockKind.Ordered(), "two", Marks.Plain),
            Block.WithText(new BlockKind.Bullet(), "dot", Marks.Plain),
        ]);
        var l = Layout(d);
        using var bmp = new SKBitmap(200, (int)l.Height + 4);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.White);
            l.Paint(canvas, d, Sel.Caret(default));
        }

        bool InkLeftOf(int y0, int y1, int x)
        {
            for (var y = y0; y < y1; y++)
            {
                for (var xx = 0; xx < x; xx++)
                {
                    if (bmp.GetPixel(xx, y).Red < 160)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        foreach (var g in l.Blocks)
        {
            Assert.True(InkLeftOf((int)g.Y, (int)(g.Y + g.Height), (int)g.TextX), "no marker ink left of the text");
        }
    }

    [Fact]
    public void Unchanged_blocks_come_from_the_cache_and_an_edit_lays_out_one_block()
    {
        var d = new Doc(Enumerable.Range(0, 60).Select(i => Block.Para($"Paragraph number {i} with a little text to lay out.")));
        var l = Layout(d);
        Assert.Equal(60, l.LastBuildMisses);
        l.Build(d, 400f);
        Assert.Equal(0, l.LastBuildMisses);
        d.Blocks[30] = Block.Para("Edited paragraph.");
        l.Build(d, 400f);
        Assert.Equal(1, l.LastBuildMisses);
        l.Build(d, 380f);
        Assert.Equal(60, l.LastBuildMisses);
    }

    [Fact]
    public void Paint_skips_blocks_outside_the_visible_slice()
    {
        var d = new Doc(Enumerable.Range(0, 80).Select(i => Block.Para($"Line {i}")));
        var l = Layout(d);
        using var bmp = new SKBitmap(300, 100);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.White);
            canvas.Translate(0, -l.Blocks[40].Y);
            l.Paint(canvas, d, Sel.Caret(default), new PaintOptions { ClipTop = l.Blocks[40].Y, ClipBottom = l.Blocks[40].Y + 100f });
        }

        var ink = 0;
        for (var y = 0; y < bmp.Height; y++)
        {
            for (var x = 0; x < bmp.Width; x++)
            {
                ink += bmp.GetPixel(x, y).Red < 128 ? 1 : 0;
            }
        }

        Assert.True(ink > 100);
    }

    [Fact]
    public void Vertical_movement_keeps_the_goal_column_across_wrapped_lines_and_blocks()
    {
        var d = new Doc([Block.Para(string.Join(' ', Enumerable.Repeat("alpha beta", 12))), Block.Para("short"), Block.Para("third paragraph")]);
        var l = Layout(d, 200f);
        Assert.True(l.Blocks[0].Text!.LineCount >= 3, $"lines {l.Blocks[0].Text!.LineCount}");
        var start = l.Caret(new Pos(0, 3), false);
        var (p1, _) = l.MoveVertical(d, new Pos(0, 3), false, 1, start.X);
        Assert.Equal(0, p1.B);
        Assert.True(p1.O > 10, $"p1 {p1}");
        var last = d.Blocks[0].Length;
        var (p2, _) = l.MoveVertical(d, new Pos(0, last), false, 1, 100f);
        Assert.Equal(1, p2.B);
        var (p3, _) = l.MoveVertical(d, new Pos(1, 0), false, -1, 10f);
        Assert.Equal(0, p3.B);
        var (top, _) = l.MoveVertical(d, new Pos(0, 2), false, -1, 5f);
        Assert.Equal(new Pos(0, 0), top);
    }

    [Fact]
    public void Home_and_end_go_to_the_edges_of_the_visual_line()
    {
        var d = new Doc([Block.Para(string.Join(' ', Enumerable.Repeat("alpha beta", 12)))]);
        var l = Layout(d, 200f);
        var mid = l.HitTest(60f, l.Blocks[0].Text!.Height / 2f).Pos;
        var home = l.MoveInLine(mid, false, end: false)!.Value.Pos;
        var end = l.MoveInLine(mid, false, end: true)!.Value.Pos;
        Assert.True(home.O < mid.O && end.O > mid.O);
        Assert.Equal(l.Caret(home, false).Y, l.Caret(mid, false).Y, 0.5);
        Assert.Equal(l.Caret(end, true).Y, l.Caret(mid, false).Y, 0.5);
    }

    [Fact]
    public void Selection_rects_include_a_stub_for_empty_lines_and_range_rects_follow_the_block()
    {
        var d = new Doc([Block.Para("abc"), new Block(new BlockKind.Para()), Block.Para("def")]);
        var l = Layout(d);
        var rects = l.SelectionRects(d, new Sel(new Pos(0, 1), new Pos(2, 2)));
        Assert.True(rects.Count >= 4);
        Assert.Contains(rects, r => r.Width == 6f);
        var rr = l.RangeRects(2, 0, 3);
        Assert.Single(rr);
        Assert.Equal(l.Blocks[2].TextY, rr[0].Y, 0.5);
    }

    [Fact]
    public void Preedit_is_shown_inline_and_widens_the_block_without_touching_the_document()
    {
        var d = new Doc([Block.Para("hello world")]);
        var l = Layout(d);
        var before = l.Caret(new Pos(0, 5), false).X;
        l.SetPreedit(new Pos(0, 5), "XXXX");
        l.Build(d, 400f);
        Assert.Equal("hello world", d.Blocks[0].Text);
        Assert.Equal(new Pos(0, 9), l.PreeditEnd);
        Assert.True(l.Caret(new Pos(0, 9), false).X > before + 10f);
        l.SetPreedit(null, null);
        l.Build(d, 400f);
        Assert.Null(l.PreeditEnd);
    }

    [Fact]
    public void Spell_marks_are_painted_under_the_misspelt_range_only()
    {
        var d = new Doc([Block.Para("good speling here")]);
        var l = Layout(d);
        using var bmp = new SKBitmap(300, 40);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.White);
            l.Paint(canvas, d, Sel.Caret(default), new PaintOptions { SpellMarks = (_, _) => [new SpellRange(5, 7)] });
        }

        var r = l.RangeRects(0, 5, 12)[0];
        var reds = 0;
        var outside = 0;
        for (var y = 0; y < bmp.Height; y++)
        {
            for (var x = 0; x < bmp.Width; x++)
            {
                var c = bmp.GetPixel(x, y);
                if (c.Red > 180 && c.Green < 120 && c.Blue < 120)
                {
                    reds++;
                    if (x < r.X - 2 || x > r.X + r.Width + 2)
                    {
                        outside++;
                    }
                }
            }
        }

        Assert.True(reds > 10);
        Assert.Equal(0, outside);
    }
}
