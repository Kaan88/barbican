// Mirrors: Rust src/rte/layout.rs (tag rust-final; parley there, Topten.RichTextKit here).
// Spike S3, docs/adr/0006-editor-engine.md. No Bulwark/RFC/Stalwart counterpart (Bulwark uses TipTap in a browser).

using Barbican.Core.Editor;
using Topten.RichTextKit;

namespace Barbican.Editor.Layout;

/// <summary>The RichTextKit (HarfBuzz shaping, UAX #9/#14, font fallback) layout engine.</summary>
public sealed class RichTextKitEngine : ITextLayoutEngine
{
    public IBlockLayout Layout(Block block, float width, LayoutStyle style)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(style);
        var tb = new TextBlock
        {
            MaxWidth = Math.Max(width, 10f),
            Alignment = block.Align switch
            {
                Align.Center => TextAlignment.Center,
                Align.Right => TextAlignment.Right,
                _ => TextAlignment.Left,
            },
        };

        var empty = block.IsEmpty;
        if (empty)
        {
            // Like Rust: an empty block is laid out as one space so it has a line and a caret.
            tb.AddText(" ", StyleMapper.ToStyle(block.Kind, Marks.Plain, style));
        }
        else
        {
            foreach (var run in block.Runs)
            {
                tb.AddText(run.Text, StyleMapper.ToStyle(block.Kind, run.Marks, style));
            }
        }

        return new RichTextKitBlockLayout(tb, block.Text, empty, style);
    }
}
