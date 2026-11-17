using Barbican.Core.Editor;
using SkiaSharp;

namespace Barbican.Editor.Layout.Tests;

/// <summary>
/// Spike S3: RichTextKit 0.6.173 on SkiaSharp 4.156 lays out, measures, hit-tests and places the
/// caret for Core/Editor blocks, with UTF-16 offsets on the outside (docs/adr/0006).
/// </summary>
public sealed class BlockLayoutTests
{
    private const string Lorem = "The quick brown fox jumps over the lazy dog, and then it keeps running across the field until the evening comes.";

    // "a", thumbs up + skin tone (4 UTF-16 units), "e" + combining acute, " ", Hebrew "shalom", " z"
    private const string Mixed = "a\U0001F44D\U0001F3FDé שלום z";

    private static readonly RichTextKitEngine Engine = new();

    [Fact]
    public void Native_skia_and_the_body_font_load()
    {
        using var tf = SKTypeface.FromFamilyName(TestFonts.Body);
        Assert.Equal(TestFonts.Body, tf.FamilyName);
        Assert.Equal(4, typeof(SKCanvas).Assembly.GetName().Version!.Major);
    }

    [Fact]
    public void Paragraph_wraps_to_the_width()
    {
        var wide = Engine.Layout(Block.Para(Lorem), 2000f, TestFonts.Style);
        var narrow = Engine.Layout(Block.Para(Lorem), 200f, TestFonts.Style);
        Assert.Equal(1, wide.LineCount);
        Assert.InRange(narrow.LineCount, 3, 8);
        Assert.True(narrow.Width <= 200f);
        // CSS line-height 1.5 at 15 px: every line is 22.5 px, whatever the font's own spacing.
        Assert.Equal(narrow.LineCount * 22.5f, narrow.Height, 0.5);
    }

    [Fact]
    public void Caret_x_grows_along_a_left_to_right_line()
    {
        var l = Engine.Layout(Block.Para("Hello world"), 2000f, TestFonts.Style);
        var xs = Enumerable.Range(0, 12).Select(o => l.Caret(o, upstream: false).X).ToArray();
        Assert.Equal(0f, xs[0], 0.5);
        for (var i = 1; i < xs.Length; i++)
        {
            Assert.True(xs[i] > xs[i - 1], $"caret x at {i} = {xs[i]} after {xs[i - 1]}");
        }

        var c = l.Caret(5, upstream: false);
        Assert.InRange(c.Height, 15f, 15f * 1.5f + 1f);
    }

    [Fact]
    public void Caret_at_a_soft_wrap_has_two_places()
    {
        var l = Engine.Layout(Block.Para(Lorem), 200f, TestFonts.Style);
        var stops = l.CaretStops();
        var top = l.Caret(0, upstream: false).Y;
        var wrap = stops.First(o => l.Caret(o, upstream: false).Y > top + 1f);
        var down = l.Caret(wrap, upstream: false);
        var up = l.Caret(wrap, upstream: true);
        Assert.Equal(top, up.Y, 0.5);
        Assert.True(down.Y > up.Y);
        Assert.Equal(0f, down.X, 0.5);
        Assert.True(up.X > 100f);
    }

    [Fact]
    public void Offsets_are_utf16_and_caret_stops_are_grapheme_boundaries()
    {
        var l = Engine.Layout(Block.Para(Mixed), 2000f, TestFonts.Style);
        var stops = l.CaretStops();
        Assert.Contains(0, stops);
        Assert.Contains(1, stops);
        Assert.Contains(5, stops);
        Assert.Contains(7, stops);
        Assert.Contains(Mixed.Length, stops);
        Assert.DoesNotContain(2, stops);
        Assert.DoesNotContain(3, stops);
        Assert.DoesNotContain(6, stops);
    }

    [Theory]
    [InlineData(Lorem, 200f, false)]
    [InlineData(Mixed, 2000f, true)]
    public void Hit_test_at_each_caret_returns_that_caret(string text, float width, bool bidi)
    {
        var l = Engine.Layout(Block.Para(text), width, TestFonts.Style);
        var misses = new List<string>();
        foreach (var o in l.CaretStops())
        {
            foreach (var up in new[] { false, true })
            {
                var c = l.Caret(o, up);
                var hit = l.HitTest(c.X, c.Y + (c.Height / 2f));
                var back = l.Caret(hit.Offset, hit.Upstream);
                if (Math.Abs(back.X - c.X) >= 0.5f || Math.Abs(back.Y - c.Y) >= 0.5f)
                {
                    misses.Add($"{o}{(up ? "^" : "")}->{hit.Offset}{(hit.Upstream ? "^" : "")} x {c.X:0.0}->{back.X:0.0}");
                }
            }
        }

        // Known: at a direction boundary (offsets 8 and 12 around the Hebrew run) two logical
        // offsets share one visual x and RichTextKit draws the logical end of the RTL run at its
        // visual left. The hit lands on the other offset. docs/adr/0006 "BiDi caret affinity".
        var report = string.Join("; ", misses);
        Assert.True(misses.Count == 0 || bidi, report);
        Assert.All(misses, m => Assert.True(m.StartsWith('8') || m.StartsWith("12", StringComparison.Ordinal), report));
    }

    [Fact]
    public void Selection_over_a_wrap_gives_one_rect_per_line()
    {
        var l = Engine.Layout(Block.Para(Lorem), 200f, TestFonts.Style);
        var rects = l.RangeRects(10, 60);
        Assert.InRange(rects.Count, 2, 4);
        Assert.True(rects[1].Y > rects[0].Y);
        Assert.All(rects, r => Assert.True(r.Width > 0f));
    }

    [Fact]
    public void Marks_change_the_shaping()
    {
        var plain = Engine.Layout(Block.Para("Barbican"), 2000f, TestFonts.Style);
        var b = Block.Para("Barbican");
        b.MapMarks(0, b.Length, m => m with { Bold = true });
        var bold = Engine.Layout(b, 2000f, TestFonts.Style);
        var h = Engine.Layout(Block.WithText(new BlockKind.Heading(1), "Barbican", Marks.Plain), 2000f, TestFonts.Style);
        Assert.True(bold.Width > plain.Width);
        Assert.True(h.Height > plain.Height);
    }

    [Fact]
    public void Empty_block_has_one_line_and_a_caret()
    {
        var l = Engine.Layout(new Block(new BlockKind.Para()), 300f, TestFonts.Style);
        Assert.Equal(1, l.LineCount);
        Assert.True(l.Caret(0, upstream: false).Height > 10f);
        Assert.Equal(new TextHit(0, Upstream: false), l.HitTest(50f, 5f));
    }
}
