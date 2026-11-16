// Mirrors: Rust src/rte/layout.rs (tag rust-final; parley there, Topten.RichTextKit here).
// Spike S3, docs/adr/0006-editor-engine.md. No Bulwark/RFC/Stalwart counterpart (Bulwark uses TipTap in a browser).

using Barbican.Core.Editor;

namespace Barbican.Editor.Layout;

/// <summary>Block spacing and type scale, ported from Rust layout.rs (Bulwark composer CSS).</summary>
public static class BlockMetrics
{
    public static float HeadingScale(int level) => level switch
    {
        1 => 24f / 14f,
        2 => 20f / 14f,
        _ => 17f / 14f,
    };

    /// <summary>Block margin (Bulwark: p 4px, h 8px, lists 4px with items 2px apart, quote/pre 8px, hr 16px).</summary>
    public static float Margin(BlockKind kind) => kind switch
    {
        BlockKind.Heading or BlockKind.Quote or BlockKind.CodeBlock or BlockKind.Cell or BlockKind.Quoted => 8f,
        BlockKind.Rule => 16f,
        _ => 4f,
    };

    public static float GapBetween(BlockKind prev, BlockKind cur)
    {
        ArgumentNullException.ThrowIfNull(prev);
        ArgumentNullException.ThrowIfNull(cur);
        if (prev.IsList && cur.IsList)
        {
            return 2f;
        }

        if ((prev == cur && cur is BlockKind.Quote or BlockKind.CodeBlock) || (prev.IsCell && cur.IsCell))
        {
            return 0f;
        }

        return Math.Max(Margin(prev), Margin(cur));
    }

    /// <summary>Left inset of a block's text (Rust: quote 19, code 13, lists 24 per level plus 24).</summary>
    public static float Inset(Block block)
    {
        ArgumentNullException.ThrowIfNull(block);
        return block.Kind switch
        {
            BlockKind.Quote => 19f,
            BlockKind.CodeBlock => 13f,
            BlockKind.Bullet or BlockKind.Ordered => 24f + (24f * block.Indent),
            _ => 0f,
        };
    }
}
