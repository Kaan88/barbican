// Mirrors: Rust src/rte/layout.rs (tag rust-final; parley there, Topten.RichTextKit here): View::build :154, block_at :264, hit :278, caret_rect :313, move_vertical :348, paint :390.
// ADR 0006, M3 plan 6.3 step 1. No Bulwark/RFC/Stalwart counterpart (Bulwark uses TipTap in a browser; its block CSS gives the spacing).

using Barbican.Core.Editor;
using SkiaSharp;

namespace Barbican.Editor.Layout;

/// <summary>
/// The whole document laid out top to bottom with Bulwark's collapsing margins: text blocks, lists with markers, quotes, code boxes, tables (a grid of cells),
/// images, rules and the collapsed quoted original. One layout per block is cached by content (an edit re-lays out only the edited block); paint draws the
/// visible slice only.
/// </summary>
public sealed class DocLayout
{
    private const int CacheLimit = 4000;
    private const float RuleHeight = 9f;
    private const float QuotedHeader = 26f;
    private const float CellPadX = 10f;
    private const float CellPadY = 6f;
    private const float DefaultImageWidth = 220f;
    private const float DefaultImageHeight = 56f;

    private readonly ITextLayoutEngine _engine;
    private readonly LayoutStyle _style;
    private readonly LayoutStyle _dimStyle;
    private readonly Dictionary<(int Hash, int Length, float Width, bool Dim), IBlockLayout> _cache = [];
    private readonly Dictionary<(string Family, bool Bold), SKTypeface> _faces = [];
    private List<BlockGeometry> _geo = [];
    private float _lastWidth;
    private (Pos At, string Text)? _preedit;

    public DocLayout(ITextLayoutEngine engine, LayoutStyle style)
    {
        _engine = engine;
        _style = style;
        _dimStyle = style with { Text = style.Dim };
    }

    public IReadOnlyList<BlockGeometry> Blocks => _geo;

    public float Height { get; private set; }

    /// <summary>The width of the last <see cref="Build"/>.</summary>
    public float Width => _lastWidth;

    /// <summary>Number of cached block layouts (tests).</summary>
    internal int CacheCount => _cache.Count;

    /// <summary>Layouts made by the last <see cref="Build"/> (cache misses; tests and the latency probe).</summary>
    public int LastBuildMisses { get; private set; }

    /// <summary>
    /// The text being composed by an IME (preedit): shown inline at <paramref name="at"/> with an underline, not in the document. Null clears it. Caret positions of that
    /// block after the insertion point are shifted by the preedit's length: use <see cref="PreeditEnd"/>.
    /// </summary>
    public void SetPreedit(Pos? at, string? text)
    {
        _preedit = at is { } p && !string.IsNullOrEmpty(text) ? (p, text) : null;
    }

    /// <summary>Where the caret sits while a preedit is shown (after it), or null without one.</summary>
    public Pos? PreeditEnd => _preedit is { } pe ? new Pos(pe.At.B, pe.At.O + pe.Text.Length) : null;

    public void Build(Doc doc, float width)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var w = Math.Max(width, 60f);
        if (Math.Abs(w - _lastWidth) > 0.5f || _cache.Count > CacheLimit)
        {
            _cache.Clear();
        }

        _lastWidth = w;
        LastBuildMisses = 0;
        var bl = doc.Blocks;
        var geo = new List<BlockGeometry>(bl.Count);
        var y = 0f;
        var i = 0;
        while (i < bl.Count)
        {
            var b = bl[i];
            if (i > 0)
            {
                y += BlockMetrics.GapBetween(bl[i - 1].Kind, b.Kind);
            }

            switch (b.Kind)
            {
                case BlockKind.Cell:
                    i = BuildTable(doc, i, w, ref y, geo);
                    continue;
                case BlockKind.Image img:
                    geo.Add(BuildImage(img.Data, b.Align, w, y));
                    y += geo[^1].Height;
                    break;
                case BlockKind.Rule:
                    geo.Add(new BlockGeometry(y, RuleHeight, 0f, y, null, 0f, w));
                    y += RuleHeight;
                    break;
                case BlockKind.Quoted q:
                    geo.Add(BuildQuoted(q, w, y));
                    y += geo[^1].Height;
                    break;
                default:
                    geo.Add(BuildText(doc, i, w, y));
                    y += geo[^1].Height;
                    break;
            }

            i++;
        }

        _geo = geo;
        Height = y;
    }

    /// <summary>The block nearest to the point's y.</summary>
    public int BlockAt(float y) => BlockAt(0f, y);

    /// <summary>The block nearest to the point (nearest vertically first, then by column for table cells).</summary>
    public int BlockAt(float x, float y)
    {
        var best = 0;
        var bestD = float.MaxValue;
        for (var i = 0; i < _geo.Count; i++)
        {
            var g = _geo[i];
            var dy = y < g.Y ? g.Y - y : y > g.Y + g.Height ? y - g.Y - g.Height : 0f;
            var dx = g.Width <= 0f ? 0f : x < g.X ? g.X - x : x > g.X + g.Width ? x - g.X - g.Width : 0f;
            var d = (dy * 1000f) + dx;
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

        var b = BlockAt(x, y);
        var g = _geo[b];
        if (g.Text is null || g.Empty)
        {
            return (new Pos(b, 0), false);
        }

        var ty = Math.Clamp(y - g.TextY, 0f, Math.Max(g.Text.Height - 1f, 0f));
        var hit = g.Text.HitTest(x - g.TextX, ty);
        return (new Pos(b, hit.Offset), hit.Upstream);
    }

    public CaretRect Caret(Pos p, bool upstream)
    {
        if (_geo.Count == 0)
        {
            return new CaretRect(0f, 0f, 18f);
        }

        var g = _geo[Math.Clamp(p.B, 0, _geo.Count - 1)];
        if (g.Text is null)
        {
            return g.Image is { } im ? new CaretRect(im.X - 3f, im.Y, im.Height) : new CaretRect(g.X, g.Y, Math.Min(g.Height, 26f));
        }

        var c = g.Text.Caret(g.Empty ? 0 : p.O, upstream);
        return c with { X = c.X + g.TextX, Y = c.Y + g.TextY };
    }

    /// <summary>The image rectangle of block <paramref name="b"/>, if it is an image.</summary>
    public LayoutRect? ImageRect(int b) => b >= 0 && b < _geo.Count ? _geo[b].Image : null;

    /// <summary>The block whose quoted-original header strip holds the point, or -1 (clicking it toggles the quote).</summary>
    public int QuotedHeaderAt(float x, float y)
    {
        for (var i = 0; i < _geo.Count; i++)
        {
            if (_geo[i].Header is { } h && x >= h.X && x <= h.X + h.Width && y >= h.Y && y <= h.Y + h.Height)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Home or End of the visual line (Rust move_in_line :327). Null for atomic blocks.</summary>
    public (Pos Pos, bool Upstream)? MoveInLine(Pos p, bool upstream, bool end)
    {
        if (p.B < 0 || p.B >= _geo.Count || _geo[p.B].Text is not { } t || _geo[p.B].Empty)
        {
            return null;
        }

        var (s, e) = t.LineRange(p.O, upstream);
        return end ? (new Pos(p.B, e), e > s) : (new Pos(p.B, s), false);
    }

    /// <summary>Vertical caret movement by <paramref name="dir"/> lines (+1 down, -1 up) keeping the goal x (Rust move_vertical :348); cells move by row.</summary>
    public (Pos Pos, bool Upstream) MoveVertical(Doc doc, Pos p, bool upstream, int dir, float goalX)
    {
        ArgumentNullException.ThrowIfNull(doc);
        if (_geo.Count == 0)
        {
            return (p, false);
        }

        var caret = Caret(p, upstream);
        var g = _geo[p.B];
        if (g.Text is { } text && !g.Empty)
        {
            var ly = caret.Y - g.TextY;
            // the caret is a little shorter than its line box, so aim into the middle of the neighbouring line
            var targetY = dir > 0 ? ly + (caret.Height * 1.5f) : ly - (caret.Height * 0.5f);
            if (targetY >= 0f && targetY < text.Height - 0.5f)
            {
                var hit = text.HitTest(goalX - g.TextX, targetY);
                return (new Pos(p.B, hit.Offset), hit.Upstream);
            }
        }

        var target = VerticalTarget(doc, p.B, dir);
        if (target is not int t)
        {
            return dir > 0 ? (new Pos(p.B, doc.Blocks[p.B].Length), false) : (new Pos(p.B, 0), false);
        }

        if (doc.Blocks[t].Kind.IsCell && !doc.Blocks[p.B].Kind.IsCell && doc.TableRange(t) is { } r)
        {
            var (rows, cols) = doc.TableDims(r);
            var cw = _lastWidth / Math.Max(cols, 1);
            var col = Math.Min((int)(goalX / cw), cols - 1);
            t = r.Start + (((dir > 0 ? 0 : rows - 1) * cols) + Math.Max(col, 0));
        }

        var tg = _geo[t];
        if (tg.Text is null || tg.Empty)
        {
            return (new Pos(t, 0), false);
        }

        var hy = dir > 0 ? 1f : tg.Text.Height - 1f;
        var h2 = tg.Text.HitTest(goalX - tg.TextX, hy);
        return (new Pos(t, h2.Offset), h2.Upstream);
    }

    /// <summary>Selection rectangles across blocks, in document coordinates (an empty or fully selected line end gets a 6 px stub, as Rust).</summary>
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
            if (g.Empty || a == z)
            {
                if (i != e.B || z > a)
                {
                    var c = Caret(new Pos(i, a), upstream: false);
                    rects.Add(new LayoutRect(c.X, c.Y, 6f, c.Height));
                }

                continue;
            }

            rects.AddRange(g.Text.RangeRects(a, z).Select(r => r with { X = r.X + g.TextX, Y = r.Y + g.TextY }));
            if (i < e.B && z == doc.Blocks[i].Length)
            {
                var c = Caret(new Pos(i, z), upstream: true);
                rects.Add(new LayoutRect(c.X, c.Y, 6f, c.Height));
            }
        }

        return rects;
    }

    /// <summary>The line rectangles of a range inside one block, in document coordinates (spelling marks, the table menu anchor).</summary>
    public IReadOnlyList<LayoutRect> RangeRects(int block, int start, int end)
    {
        if (block < 0 || block >= _geo.Count || _geo[block].Text is not { } t || _geo[block].Empty)
        {
            return [];
        }

        var g = _geo[block];
        return t.RangeRects(start, end).Select(r => r with { X = r.X + g.TextX, Y = r.Y + g.TextY }).ToArray();
    }

    /// <summary>Paints the whole document (the spike's entry point).</summary>
    public void Paint(SKCanvas canvas, Doc doc, Sel sel) => Paint(canvas, doc, sel, new PaintOptions());

    /// <summary>
    /// Paints the visible blocks: box decorations, the selection (rectangles behind the text), the text (through the contrast layer when
    /// <see cref="LayoutStyle.TextContrast"/> is above 1), then list markers, images, the quoted header and spelling marks.
    /// </summary>
    public void Paint(SKCanvas canvas, Doc doc, Sel sel, PaintOptions options)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(options);
        var (top, bottom) = (options.ClipTop ?? float.MinValue, options.ClipBottom ?? float.MaxValue);
        var (first, last) = VisibleRange(top, bottom);
        PaintBack(canvas, doc, first, last);
        if (!sel.IsCollapsed)
        {
            using var selPaint = new SKPaint { Color = _style.Selection, IsAntialias = false };
            foreach (var r in SelectionRects(doc, sel))
            {
                if (r.Y + r.Height >= top && r.Y <= bottom)
                {
                    canvas.DrawRect(r.X, r.Y, r.Width, r.Height, selPaint);
                }
            }
        }

        using var layer = _style.TextContrast > 1f ? new SKPaint { ColorFilter = ContrastFilter(_style.TextContrast) } : null;
        if (layer is not null)
        {
            canvas.SaveLayer(layer);
        }

        for (var i = first; i <= last; i++)
        {
            var g = _geo[i];
            if (g.Text is not null && !g.Empty)
            {
                g.Text.Paint(canvas, g.TextX, g.TextY, 0, 0);
            }
        }

        if (layer is not null)
        {
            canvas.Restore();
        }

        PaintFront(canvas, doc, sel, options, first, last);
    }

    internal static string Alpha(int n)
    {
        var s = new System.Text.StringBuilder();
        while (n > 0)
        {
            n--;
            s.Insert(0, (char)('a' + (n % 26)));
            n /= 26;
        }

        return s.ToString();
    }

    internal static string Roman(int n)
    {
        (int V, string R)[] table = [(1000, "m"), (900, "cm"), (500, "d"), (400, "cd"), (100, "c"), (90, "xc"), (50, "l"), (40, "xl"), (10, "x"), (9, "ix"), (5, "v"), (4, "iv"), (1, "i")];
        var s = new System.Text.StringBuilder();
        foreach (var (v, r) in table)
        {
            while (n >= v)
            {
                s.Append(r);
                n -= v;
            }
        }

        return s.ToString();
    }

    private static SKColorFilter ContrastFilter(float k)
    {
        var table = new byte[256];
        var identity = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            table[i] = (byte)Math.Round(255.0 * (1.0 - Math.Pow(1.0 - (i / 255.0), k)));
            identity[i] = (byte)i;
        }

        return SKColorFilter.CreateTable(table, identity, identity, identity);
    }

    private static int? VerticalTarget(Doc doc, int b, int dir)
    {
        if (doc.Blocks[b].Kind is BlockKind.Cell cell && doc.TableRange(b) is { } r)
        {
            var (rows, cols) = doc.TableDims(r);
            var nr = cell.Row + dir;
            if (nr < 0)
            {
                return r.Start > 0 ? r.Start - 1 : null;
            }

            if (nr >= rows)
            {
                return r.End < doc.Blocks.Count ? r.End : null;
            }

            return r.Start + (nr * cols) + cell.Col;
        }

        var t = b + dir;
        return t >= 0 && t < doc.Blocks.Count ? t : null;
    }

    private static BlockGeometry BuildImage(ImageData img, Align align, float w, float y)
    {
        var decoded = LayoutImages.Decode(img.Bytes);
        var (nw, nh) = decoded is null ? (0, 0) : (decoded.Width, decoded.Height);
        float iw;
        float ih;
        if (img.Width > 0)
        {
            iw = img.Width;
            ih = img.Height > 0 ? img.Height : nw > 0 ? img.Width * nh / (float)nw : 120f;
        }
        else if (nw > 0)
        {
            (iw, ih) = (nw, nh);
        }
        else
        {
            (iw, ih) = (DefaultImageWidth, DefaultImageHeight);
        }

        if (iw > w)
        {
            ih *= w / iw;
            iw = w;
        }

        var ix = align switch
        {
            Align.Center => (w - iw) / 2f,
            Align.Right => w - iw,
            _ => 0f,
        };
        return new BlockGeometry(y, ih, ix, y, null, 0f, w, false, null, new LayoutRect(ix, y, iw, ih));
    }

    private static void Squiggle(SKCanvas canvas, float x0, float x1, float y, SKPaint paint)
    {
        using var pb = new SKPathBuilder();
        pb.MoveTo(x0, y);
        var up = true;
        for (var x = x0 + 2f; x < x1; x += 2f)
        {
            pb.LineTo(x, up ? y - 1.5f : y);
            up = !up;
        }

        pb.LineTo(x1, up ? y : y - 1.5f);
        using var path = pb.Detach();
        canvas.DrawPath(path, paint);
    }

    private (int First, int Last) VisibleRange(float top, float bottom)
    {
        if (_geo.Count == 0)
        {
            return (0, -1);
        }

        var first = _geo.Count;
        var last = -1;
        for (var i = 0; i < _geo.Count; i++)
        {
            var g = _geo[i];
            if (g.Y + g.Height + 8f >= top && g.Y - 8f <= bottom)
            {
                first = Math.Min(first, i);
                last = Math.Max(last, i);
            }
        }

        return (first, last);
    }

    private IBlockLayout TextLayout(Block block, float width, bool dim)
    {
        var h = new HashCode();
        h.Add(block.Kind);
        h.Add(block.Align);
        foreach (var r in block.Runs)
        {
            h.Add(r);
        }

        var key = (h.ToHashCode(), block.Length, width, dim);
        if (_cache.TryGetValue(key, out var hit))
        {
            return hit;
        }

        LastBuildMisses++;
        var l = _engine.Layout(block, width, dim ? _dimStyle : _style);
        _cache[key] = l;
        return l;
    }

    private BlockGeometry BuildText(Doc doc, int i, float w, float y)
    {
        var b = doc.Blocks[i];
        var prev = i > 0 ? doc.Blocks[i - 1].Kind : null;
        var next = i + 1 < doc.Blocks.Count ? doc.Blocks[i + 1].Kind : null;
        var (inset, padTop, padBottom) = b.Kind switch
        {
            BlockKind.Quote => (19f, prev is BlockKind.Quote ? 0f : 2f, next is BlockKind.Quote ? 0f : 2f),
            BlockKind.CodeBlock => (13f, prev is BlockKind.CodeBlock ? 0f : 12f, next is BlockKind.CodeBlock ? 0f : 12f),
            BlockKind.Bullet or BlockKind.Ordered => (24f + (24f * b.Indent), 0f, 0f),
            _ => (0f, 0f, 0f),
        };
        var tw = w - inset - (b.Kind is BlockKind.CodeBlock ? 13f : 0f);
        var shown = WithPreedit(b, i);
        var text = TextLayout(shown, tw, b.Kind is BlockKind.Quote);
        var h = padTop + text.Height + padBottom;
        return new BlockGeometry(y, h, inset, y + padTop, text, 0f, w, b.IsEmpty && ReferenceEquals(shown, b));
    }

    private Block WithPreedit(Block b, int index)
    {
        if (_preedit is not { } pe || pe.At.B != index)
        {
            return b;
        }

        var clone = new Block(b.Kind) { Align = b.Align, Indent = b.Indent };
        var at = Math.Clamp(pe.At.O, 0, b.Length);
        var acc = 0;
        var done = false;
        foreach (var run in b.Runs)
        {
            var end = acc + run.Text.Length;
            if (!done && at <= end)
            {
                var cut = at - acc;
                if (cut > 0)
                {
                    clone.Runs.Add(new Run(run.Text[..cut], run.Marks));
                }

                clone.Runs.Add(new Run(pe.Text, run.Marks with { Underline = true }));
                if (cut < run.Text.Length)
                {
                    clone.Runs.Add(new Run(run.Text[cut..], run.Marks));
                }

                done = true;
            }
            else
            {
                clone.Runs.Add(run);
            }

            acc = end;
        }

        if (!done)
        {
            clone.Runs.Add(new Run(pe.Text, Marks.Plain with { Underline = true }));
        }

        return clone;
    }

    private int BuildTable(Doc doc, int i, float w, ref float y, List<BlockGeometry> geo)
    {
        var r = doc.TableRange(i) ?? new BlockSpan(i, i + 1);
        var (rows, cols) = doc.TableDims(r);
        var cw = MathF.Floor(w / Math.Max(cols, 1));
        var rowH = new float[Math.Max(rows, 1)];
        var cells = new List<(Block B, BlockKind.Cell C, IBlockLayout L)>(r.Count);
        for (var k = r.Start; k < r.End; k++)
        {
            if (doc.Blocks[k].Kind is BlockKind.Cell c)
            {
                var l = TextLayout(WithPreedit(doc.Blocks[k], k), cw - (2 * CellPadX), dim: false);
                rowH[c.Row] = Math.Max(rowH[c.Row], l.Height + (2 * CellPadY));
                cells.Add((doc.Blocks[k], c, l));
            }
        }

        var rowY = new float[rowH.Length];
        rowY[0] = y;
        for (var rr = 1; rr < rowY.Length; rr++)
        {
            rowY[rr] = rowY[rr - 1] + rowH[rr - 1];
        }

        foreach (var (b, c, l) in cells)
        {
            geo.Add(new BlockGeometry(rowY[c.Row], rowH[c.Row], (c.Col * cw) + CellPadX, rowY[c.Row] + CellPadY, l, c.Col * cw, cw, b.IsEmpty));
        }

        y = rowY[^1] + rowH[^1];
        return r.End;
    }

    private BlockGeometry BuildQuoted(BlockKind.Quoted q, float w, float y)
    {
        var header = new LayoutRect(0f, y, 180f, QuotedHeader);
        if (!q.Open)
        {
            return new BlockGeometry(y, QuotedHeader, 0f, y, null, 0f, w, false, header);
        }

        var inset = q.Cite ? 14f : 0f;
        var l = TextLayout(Block.Para(q.Text), w - inset, dim: true);
        return new BlockGeometry(y, QuotedHeader + 6f + l.Height, inset, y + QuotedHeader + 6f, l, 0f, w, false, header);
    }

    private SKTypeface Face(string family, bool bold)
    {
        if (!_faces.TryGetValue((family, bold), out var f))
        {
            f = SKTypeface.FromFamilyName(family, bold ? SKFontStyle.Bold : SKFontStyle.Normal) ?? SKTypeface.Default;
            _faces[(family, bold)] = f;
        }

        return f;
    }

    private void PaintBack(SKCanvas canvas, Doc doc, int first, int last)
    {
        using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        using var stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f, Color = _style.Line };
        for (var i = first; i <= last && i < doc.Blocks.Count; i++)
        {
            var b = doc.Blocks[i];
            var g = _geo[i];
            switch (b.Kind)
            {
                case BlockKind.Quote:
                {
                    var top = i > 0 && doc.Blocks[i - 1].Kind is BlockKind.Quote ? _geo[i - 1].Y + _geo[i - 1].Height : g.Y;
                    fill.Color = _style.Line;
                    canvas.DrawRect(0f, top, 3f, g.Y + g.Height - top, fill);
                    break;
                }

                case BlockKind.CodeBlock when i == 0 || doc.Blocks[i - 1].Kind is not BlockKind.CodeBlock:
                {
                    var j = i;
                    while (j + 1 < doc.Blocks.Count && doc.Blocks[j + 1].Kind is BlockKind.CodeBlock)
                    {
                        j++;
                    }

                    var rect = new SKRect(0.5f, g.Y + 0.5f, g.Width - 0.5f, _geo[j].Y + _geo[j].Height - 0.5f);
                    fill.Color = _style.CodeBg;
                    canvas.DrawRoundRect(rect, 6f, 6f, fill);
                    canvas.DrawRoundRect(rect, 6f, 6f, stroke);
                    break;
                }

                case BlockKind.Cell cell:
                {
                    var rect = new SKRect(g.X + 0.5f, g.Y + 0.5f, g.X + g.Width + 1f, g.Y + g.Height + 1f);
                    if (cell.Header)
                    {
                        fill.Color = _style.HeaderBg;
                        canvas.DrawRect(rect, fill);
                    }

                    canvas.DrawRect(rect, stroke);
                    break;
                }

                case BlockKind.Rule:
                    fill.Color = _style.Line;
                    canvas.DrawRect(0f, g.Y + 4f, g.Width, 1f, fill);
                    break;
                case BlockKind.Quoted q:
                    PaintQuotedHeader(canvas, q, g, fill, stroke, selected: false);
                    break;
            }
        }
    }

    private void PaintQuotedHeader(SKCanvas canvas, BlockKind.Quoted q, BlockGeometry g, SKPaint fill, SKPaint stroke, bool selected)
    {
        if (g.Header is not { } h)
        {
            return;
        }

        var rect = new SKRect(h.X + 0.5f, h.Y + 0.5f, h.X + h.Width - 0.5f, h.Y + h.Height - 0.5f);
        fill.Color = _style.CodeBg;
        canvas.DrawRoundRect(rect, 6f, 6f, fill);
        stroke.Color = selected ? _style.Accent : _style.Line;
        canvas.DrawRoundRect(rect, 6f, 6f, stroke);
        stroke.Color = _style.Line;
        var label = q.Open ? (q.Cite ? "Hide quoted text" : "Hide forwarded message") : (q.Cite ? "Show quoted text" : "Show forwarded message");
        using var chevron = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, Color = _style.Dim, StrokeCap = SKStrokeCap.Round };
        var cx = h.X + 16f;
        var cy = h.Y + (h.Height / 2f);
        using var pb = new SKPathBuilder();
        if (q.Open)
        {
            pb.MoveTo(cx - 4f, cy - 2f);
            pb.LineTo(cx, cy + 2f);
            pb.LineTo(cx + 4f, cy - 2f);
        }
        else
        {
            pb.MoveTo(cx - 2f, cy - 4f);
            pb.LineTo(cx + 2f, cy);
            pb.LineTo(cx - 2f, cy + 4f);
        }

        using var path = pb.Detach();
        canvas.DrawPath(path, chevron);
        using var font = new SKFont(Face(_style.BodyFont, bold: false), _style.BaseSize * 0.9f) { Edging = _style.Edging, Subpixel = true };
        using var textPaint = new SKPaint { IsAntialias = true, Color = _style.Dim };
        var m = font.Metrics;
        canvas.DrawText(label, h.X + 28f, cy - ((m.Ascent + m.Descent) / 2f), SKTextAlign.Left, font, textPaint);
    }

    private void PaintFront(SKCanvas canvas, Doc doc, Sel sel, PaintOptions options, int first, int last)
    {
        using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = _style.Text };
        using var stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, Color = _style.Text };
        using var frame = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f, Color = _style.Line };
        var (ss, se) = (sel.Start, sel.End);
        for (var i = first; i <= last && i < doc.Blocks.Count; i++)
        {
            var b = doc.Blocks[i];
            var g = _geo[i];
            var inSel = !sel.IsCollapsed && i >= ss.B && i <= se.B;
            var focused = inSel || (sel.IsCollapsed && sel.Focus.B == i);
            switch (b.Kind)
            {
                case BlockKind.Bullet or BlockKind.Ordered:
                    PaintMarker(canvas, doc, i, g, fill, stroke);
                    break;
                case BlockKind.Image img:
                    PaintImage(canvas, img.Data, g, focused || options.SelectedImage == i, frame);
                    break;
                case BlockKind.Quoted q:
                    if (focused)
                    {
                        PaintQuotedHeader(canvas, q, g, fill, frame, selected: true);
                    }

                    break;
                case BlockKind.Rule when inSel:
                    fill.Color = _style.Selection;
                    canvas.DrawRect(g.X, g.Y, g.Width, g.Height, fill);
                    fill.Color = _style.Text;
                    break;
            }
        }

        if (options.SpellMarks is { } marks)
        {
            using var spell = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f, Color = _style.SpellUnderline };
            for (var i = first; i <= last && i < doc.Blocks.Count; i++)
            {
                var g = _geo[i];
                if (g.Text is null || g.Empty || doc.Blocks[i].Kind is BlockKind.CodeBlock)
                {
                    continue;
                }

                foreach (var range in marks(i, doc.Blocks[i].Text))
                {
                    foreach (var r in RangeRects(i, range.Start, range.End))
                    {
                        Squiggle(canvas, r.X, r.X + r.Width, r.Y + r.Height - 2f, spell);
                    }
                }
            }
        }
    }

    private void PaintMarker(SKCanvas canvas, Doc doc, int i, BlockGeometry g, SKPaint fill, SKPaint stroke)
    {
        if (g.Text is null)
        {
            return;
        }

        var b = doc.Blocks[i];
        var c = g.Text.Caret(0, upstream: false);
        var midY = g.TextY + c.Y + (c.Height / 2f);
        var right = g.TextX - 8f;
        if (b.Kind is BlockKind.Ordered)
        {
            var n = doc.Ordinal(i);
            var marker = (b.Indent % 3) switch
            {
                1 => Alpha(n) + ".",
                2 => Roman(n) + ".",
                _ => n.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".",
            };
            using var font = new SKFont(Face(_style.BodyFont, bold: false), _style.BaseSize) { Edging = _style.Edging, Subpixel = true };
            var m = font.Metrics;
            canvas.DrawText(marker, right, midY - ((m.Ascent + m.Descent) / 2f), SKTextAlign.Right, font, fill);
            return;
        }

        var cx = right - 3f;
        switch (b.Indent % 3)
        {
            case 0:
                canvas.DrawCircle(cx, midY, 2.6f, fill);
                break;
            case 1:
                canvas.DrawCircle(cx, midY, 2.4f, stroke);
                break;
            default:
                canvas.DrawRect(cx - 2.3f, midY - 2.3f, 4.6f, 4.6f, fill);
                break;
        }
    }

    private void PaintImage(SKCanvas canvas, ImageData img, BlockGeometry g, bool selected, SKPaint frame)
    {
        if (g.Image is not { } r)
        {
            return;
        }

        var rect = new SKRect(r.X, r.Y, r.X + r.Width, r.Y + r.Height);
        var decoded = LayoutImages.Decode(img.Bytes);
        if (decoded is not null)
        {
            using var paint = new SKPaint { IsAntialias = true };
            canvas.DrawImage(decoded, rect, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), paint);
        }
        else
        {
            using var back = new SKPaint { Color = _style.CodeBg, IsAntialias = true };
            canvas.DrawRoundRect(rect, 4f, 4f, back);
            canvas.DrawRoundRect(rect, 4f, 4f, frame);
            using var font = new SKFont(Face(_style.BodyFont, bold: false), _style.BaseSize * 0.85f) { Edging = _style.Edging, Subpixel = true };
            using var text = new SKPaint { Color = _style.Dim, IsAntialias = true };
            var label = img.Alt.Length > 0 ? img.Alt : img.Name.Length > 0 ? img.Name : "Image";
            var m = font.Metrics;
            canvas.Save();
            canvas.ClipRect(rect);
            canvas.DrawText(label, r.X + 8f, r.Y + (r.Height / 2f) - ((m.Ascent + m.Descent) / 2f), SKTextAlign.Left, font, text);
            canvas.Restore();
        }

        if (selected)
        {
            using var sel = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f, Color = _style.Accent };
            canvas.DrawRect(new SKRect(rect.Left - 1f, rect.Top - 1f, rect.Right + 1f, rect.Bottom + 1f), sel);
        }
    }
}
