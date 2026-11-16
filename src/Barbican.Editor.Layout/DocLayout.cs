// Mirrors: Rust src/rte/layout.rs (tag rust-final; parley there, Topten.RichTextKit here).
// Spike S3, docs/adr/0006-editor-engine.md. No Bulwark/RFC/Stalwart counterpart (Bulwark uses TipTap in a browser).

using Barbican.Core.Editor;
using SkiaSharp;

namespace Barbican.Editor.Layout;

/// <summary>
/// The whole document laid out top to bottom (Rust layout.rs <c>View::build</c>, the spike slice:
/// text blocks, quotes, code and lists with Bulwark's collapsing margins; tables, images and the
/// quoted original get a fixed-height placeholder box until M3).
/// </summary>
public sealed class DocLayout
{
    private const float AtomicHeight = 26f;
    private readonly ITextLayoutEngine _engine;
    private readonly LayoutStyle _style;
    private List<BlockGeometry> _geo = [];

    public DocLayout(ITextLayoutEngine engine, LayoutStyle style)
    {
        _engine = engine;
        _style = style;
    }

    public IReadOnlyList<BlockGeometry> Blocks => _geo;

    public float Height { get; private set; }

    public void Build(Doc doc, float width)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var w = Math.Max(width, 60f);
        var geo = new List<BlockGeometry>(doc.Blocks.Count);
        var y = 0f;
        for (var i = 0; i < doc.Blocks.Count; i++)
        {
            var b = doc.Blocks[i];
            if (i > 0)
            {
                y += BlockMetrics.GapBetween(doc.Blocks[i - 1].Kind, b.Kind);
            }

            if (b.Kind.IsAtomic)
            {
                geo.Add(new BlockGeometry(y, AtomicHeight, 0f, y, Text: null));
                y += AtomicHeight;
                continue;
            }

            var inset = BlockMetrics.Inset(b);
            var text = _engine.Layout(b, w - inset, _style);
            geo.Add(new BlockGeometry(y, text.Height, inset, y, text));
            y += text.Height;
        }

        _geo = geo;
        Height = y;
    }

    /// <summary>The block nearest to y (Rust block_at, without the column test for cells).</summary>
    public int BlockAt(float y)
    {
        var best = 0;
        var bestD = float.MaxValue;
        for (var i = 0; i < _geo.Count; i++)
        {
            var g = _geo[i];
            var d = y < g.Y ? g.Y - y : y > g.Y + g.Height ? y - g.Y - g.Height : 0f;
            if (d < bestD)
            {
                bestD = d;
                best = i;
            }
        }

        return best;
    }

    public (Pos Pos, bool Upstream) HitTest(float x, float y)
    {
        if (_geo.Count == 0)
        {
            return (default, false);
        }

        var b = BlockAt(y);
        var g = _geo[b];
        if (g.Text is null)
        {
            return (new Pos(b, 0), false);
        }

        var ty = Math.Clamp(y - g.TextY, 0f, Math.Max(g.Text.Height - 1f, 0f));
        var hit = g.Text.HitTest(x - g.TextX, ty);
        return (new Pos(b, hit.Offset), hit.Upstream);
    }

    public CaretRect Caret(Pos p, bool upstream)
    {
        var g = _geo[Math.Clamp(p.B, 0, _geo.Count - 1)];
        if (g.Text is null)
        {
            return new CaretRect(0f, g.Y, Math.Min(g.Height, AtomicHeight));
        }

        var c = g.Text.Caret(p.O, upstream);
        return c with { X = c.X + g.TextX, Y = c.Y + g.TextY };
    }

    /// <summary>Selection rectangles across blocks, in document coordinates.</summary>
    public IReadOnlyList<LayoutRect> SelectionRects(Doc doc, Sel sel)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var (s, e) = (sel.Start, sel.End);
        var rects = new List<LayoutRect>();
        for (var i = s.B; i <= e.B && i < _geo.Count; i++)
        {
            var g = _geo[i];
            if (g.Text is null)
            {
                continue;
            }

            var a = i == s.B ? s.O : 0;
            var z = i == e.B ? e.O : doc.Blocks[i].Length;
            rects.AddRange(g.Text.RangeRects(a, z).Select(r => r with { X = r.X + g.TextX, Y = r.Y + g.TextY }));
        }

        return rects;
    }

    public void Paint(SKCanvas canvas, Doc doc, Sel sel)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(doc);
        var (s, e) = (sel.Start, sel.End);
        for (var i = 0; i < _geo.Count; i++)
        {
            var g = _geo[i];
            if (g.Text is null)
            {
                continue;
            }

            var a = i == s.B ? s.O : i > s.B ? 0 : int.MaxValue;
            var z = i == e.B ? e.O : i < e.B ? doc.Blocks[i].Length : 0;
            g.Text.Paint(canvas, g.TextX, g.TextY, a, z);
        }
    }
}
