using Barbican.Core.Editor;
using SkiaSharp;

namespace Barbican.Editor.Layout.Tests;

/// <summary>Spike S3: a Core/Editor document laid out, hit-tested and painted end to end.</summary>
public sealed class DocLayoutTests
{
    private static Doc Sample()
    {
        var d = new Doc(
        [
            Block.WithText(new BlockKind.Heading(2), "Quarterly notes", Marks.Plain),
            Block.Para("Hello team, here is a short summary of what changed this week."),
            Block.WithText(new BlockKind.Bullet(), "First point", Marks.Plain),
            Block.WithText(new BlockKind.Bullet(), "Second point", Marks.Plain),
            new Block(new BlockKind.Rule()),
            Block.Para("Thanks"),
        ]);
        d.Blocks[1].MapMarks(11, 15, m => m with { Bold = true });
        d.Fix();
        return d;
    }

    [Fact]
    public void Blocks_stack_with_bulwark_margins()
    {
        var d = Sample();
        var layout = new DocLayout(new RichTextKitEngine(), TestFonts.Style);
        layout.Build(d, 400f);
        var g = layout.Blocks;
        Assert.Equal(d.Blocks.Count, g.Count);
        Assert.Equal(g[0].Y + g[0].Height + 8f, g[1].Y, 0.01);
        Assert.Equal(g[2].Y + g[2].Height + 2f, g[3].Y, 0.01);
        Assert.Equal(24f, g[2].TextX);
        Assert.Null(g[4].Text);
        Assert.Equal(g[^1].Y + g[^1].Height, layout.Height, 0.01);
    }

    [Fact]
    public void Hit_and_caret_agree_across_blocks()
    {
        var d = Sample();
        var layout = new DocLayout(new RichTextKitEngine(), TestFonts.Style);
        layout.Build(d, 400f);
        var p = new Pos(3, 7);
        var c = layout.Caret(p, upstream: false);
        Assert.True(c.X > 24f);
        var (hit, _) = layout.HitTest(c.X, c.Y + (c.Height / 2f));
        Assert.Equal(p, hit);
        var (below, _) = layout.HitTest(5f, layout.Height + 100f);
        Assert.Equal(d.Blocks.Count - 1, below.B);
    }

    [Fact]
    public void Changing_palette_rebuilds_cached_text_and_keeps_preedit()
    {
        var doc = Doc.FromText("One block\nAnother block");
        var layout = new DocLayout(new RichTextKitEngine(), TestFonts.Style);
        layout.SetPreedit(new Pos(1, 0), "Text");
        layout.Build(doc, 400f);
        var before = layout.Blocks[0].Text;
        layout.Build(doc, 400f);
        Assert.Same(before, layout.Blocks[0].Text);

        var dark = TestFonts.Style with { Text = SKColors.White };
        Assert.True(layout.SetStyle(dark));
        layout.Build(doc, 400f);

        Assert.NotSame(before, layout.Blocks[0].Text);
        Assert.Equal(new Pos(1, 4), layout.PreeditEnd);
        Assert.False(layout.SetStyle(dark));
    }

    [Fact]
    public void Selection_spans_blocks_and_paints_ink()
    {
        var d = Sample();
        var layout = new DocLayout(new RichTextKitEngine(), TestFonts.Style);
        layout.Build(d, 400f);
        var sel = new Sel(new Pos(1, 6), new Pos(3, 6));
        var rects = layout.SelectionRects(d, sel);
        Assert.True(rects.Count >= 3);

        using var bmp = new SKBitmap(420, (int)Math.Ceiling(layout.Height) + 10);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.White);
            layout.Paint(canvas, d, sel);
        }

        var ink = 0;
        for (var y = 0; y < bmp.Height; y++)
        {
            for (var x = 0; x < bmp.Width; x++)
            {
                if (bmp.GetPixel(x, y).Red < 128)
                {
                    ink++;
                }
            }
        }

        Assert.True(ink > 500, $"only {ink} dark pixels painted");
        var outDir = Environment.GetEnvironmentVariable("BARBICAN_S3_OUT");
        if (!string.IsNullOrEmpty(outDir))
        {
            using var png = bmp.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(Path.Combine(outDir, $"doc-layout-{(OperatingSystem.IsWindows() ? "win" : "linux")}.png"), png.ToArray());
        }
    }

    [Fact]
    public void Text_contrast_darkens_glyph_edges_only()
    {
        var d = Doc.FromText("Contrast check: the quick brown fox");
        long Ink(float k)
        {
            var layout = new DocLayout(new RichTextKitEngine(), TestFonts.Style with { TextContrast = k });
            layout.Build(d, 400f);
            using var bmp = new SKBitmap(400, 40);
            using (var canvas = new SKCanvas(bmp))
            {
                canvas.Clear(SKColors.White);
                layout.Paint(canvas, d, Sel.Caret(default));
            }

            long sum = 0;
            for (var y = 0; y < bmp.Height; y++)
            {
                for (var x = 0; x < bmp.Width; x++)
                {
                    sum += 255 - bmp.GetPixel(x, y).Green;
                }
            }

            return sum;
        }

        var plain = Ink(1f);
        var boosted = Ink(1.6f);
        Assert.InRange((double)boosted / plain, 1.05, 1.35);
    }
}
