// Mirrors: Rust src/rte/layout.rs (tag rust-final; parley there, Topten.RichTextKit here).
// Spike S3, docs/adr/0006-editor-engine.md. No Bulwark/RFC/Stalwart counterpart (Bulwark uses TipTap in a browser).

using SkiaSharp;
using Topten.RichTextKit;

namespace Barbican.Editor.Layout;

/// <summary>
/// A RichTextKit <see cref="TextBlock"/> behind the UTF-16 interface. RichTextKit indexes UTF-32
/// code points; <see cref="StyledText.CharacterToCodePointIndex"/> and back do the mapping.
/// </summary>
internal sealed class RichTextKitBlockLayout : IBlockLayout
{
    private readonly TextBlock _tb;
    private readonly bool _empty;
    private readonly LayoutStyle _style;
    private readonly string _text;
    private readonly int _utf16Length;

    public RichTextKitBlockLayout(TextBlock tb, string text, bool empty, LayoutStyle style)
    {
        _tb = tb;
        _text = text;
        _utf16Length = text.Length;
        _empty = empty;
        _style = style;
        tb.Layout();
    }

    public float Width => _tb.MeasuredWidth;

    public float Height => _tb.MeasuredHeight;

    public int LineCount => _tb.LineCount;

    /// <summary>The underlying block, for engine-specific tests and the S3 harness.</summary>
    internal TextBlock TextBlock => _tb;

    public CaretRect Caret(int offset, bool upstream)
    {
        var cp = _empty ? 0 : ToCodePoint(offset);
        var ci = _tb.GetCaretInfo(new CaretPosition(cp, upstream));
        var r = ci.CaretRectangle;
        return new CaretRect(r.Left, r.Top, r.Height);
    }

    public TextHit HitTest(float x, float y)
    {
        if (_empty)
        {
            return new TextHit(0, Upstream: false);
        }

        var h = _tb.HitTest(x, y);
        var cp = h.CaretPosition;
        return new TextHit(ToUtf16(cp.CodePointIndex), cp.AltPosition);
    }

    public IReadOnlyList<LayoutRect> RangeRects(int startOffset, int endOffset)
    {
        if (_empty || startOffset >= endOffset)
        {
            return [];
        }

        var (a, b) = (ToCodePoint(startOffset), ToCodePoint(endOffset));
        var rects = new List<LayoutRect>();
        foreach (var line in _tb.Lines)
        {
            var (s, e) = (Math.Max(a, line.Start), Math.Min(b, line.End));
            if (s >= e)
            {
                continue;
            }

            var x0 = _tb.GetCaretInfo(new CaretPosition(s)).CaretXCoord;
            var x1 = _tb.GetCaretInfo(new CaretPosition(e, altPosition: e == line.End)).CaretXCoord;
            rects.Add(new LayoutRect(Math.Min(x0, x1), line.YCoord, Math.Abs(x1 - x0), line.Height));
        }

        return rects;
    }

    public IReadOnlyList<int> CaretStops()
    {
        if (_empty)
        {
            return [0];
        }

        return _tb.CaretIndicies.Select(ToUtf16).Distinct().ToArray();
    }

    public (int Start, int End) LineRange(int offset, bool upstream)
    {
        if (_empty || _tb.Lines.Count == 0)
        {
            return (0, 0);
        }

        var cp = ToCodePoint(offset);
        var lines = _tb.Lines;
        var hit = lines[^1];
        foreach (var line in lines)
        {
            var inside = upstream ? cp > line.Start && cp <= line.End : cp >= line.Start && cp < line.End;
            if (inside || (cp == line.Start && cp == line.End))
            {
                hit = line;
                break;
            }
        }

        var start = ToUtf16(hit.Start);
        var end = ToUtf16(hit.End);
        if (end > start && end <= _text.Length && _text[end - 1] == (char)10)
        {
            end--;
        }

        return (start, Math.Max(start, end));
    }

    public void Paint(SKCanvas canvas, float x, float y, int selStart, int selEnd)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        var options = new TextPaintOptions
        {
            Edging = _style.Edging,
            Hinting = _style.Hinting,
            SubpixelPositioning = _style.SubpixelPositioning,
            SelectionColor = _style.Selection,
        };
        if (!_empty && selStart < selEnd)
        {
            options.Selection = new TextRange(ToCodePoint(selStart), ToCodePoint(selEnd));
        }

        if (_empty)
        {
            return;
        }

        _tb.Paint(canvas, new SKPoint(x, y), options);
    }

    private int ToCodePoint(int utf16) => _tb.CharacterToCodePointIndex(Math.Clamp(utf16, 0, _utf16Length));

    private int ToUtf16(int codePoint) => _tb.CodePointToCharacterIndex(codePoint);
}
