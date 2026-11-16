// Mirrors: Rust src/rte/layout.rs (tag rust-final; parley there, Topten.RichTextKit here).
// Spike S3, docs/adr/0006-editor-engine.md. No Bulwark/RFC/Stalwart counterpart (Bulwark uses TipTap in a browser).

using SkiaSharp;

namespace Barbican.Editor.Layout;

/// <summary>
/// The laid-out text of one block. All offsets are UTF-16 code units into the block text (the
/// Core/Editor convention); an engine that counts code points maps internally. Coordinates are
/// relative to the block's text origin.
/// </summary>
public interface IBlockLayout
{
    float Width { get; }

    float Height { get; }

    int LineCount { get; }

    CaretRect Caret(int offset, bool upstream);

    TextHit HitTest(float x, float y);

    /// <summary>One rectangle per line covered by [startOffset, endOffset).</summary>
    IReadOnlyList<LayoutRect> RangeRects(int startOffset, int endOffset);

    /// <summary>Caret stops (UTF-16 offsets) in logical order, including 0 and the text length.</summary>
    IReadOnlyList<int> CaretStops();

    void Paint(SKCanvas canvas, float x, float y, int selStart, int selEnd);
}
