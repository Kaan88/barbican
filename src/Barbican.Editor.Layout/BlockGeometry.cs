// Mirrors: Rust src/rte/layout.rs (tag rust-final; parley there, Topten.RichTextKit here).
// Spike S3, docs/adr/0006-editor-engine.md. No Bulwark/RFC/Stalwart counterpart (Bulwark uses TipTap in a browser).

namespace Barbican.Editor.Layout;

/// <summary>Where one block sits in the document (Rust layout.rs <c>Geo</c>).</summary>
/// <param name="Y">Top of the block's box (a table cell: the top of its row).</param>
/// <param name="Height">Height of the box.</param>
/// <param name="TextX">Origin of the text layout.</param>
/// <param name="TextY">Origin of the text layout.</param>
/// <param name="Text">The laid-out text; null for atomic blocks.</param>
/// <param name="X">Left of the box (a table cell: its column).</param>
/// <param name="Width">Width of the box.</param>
/// <param name="Empty">The block has no text (its layout is one placeholder space).</param>
/// <param name="Header">The header strip of a quoted block (clicking it toggles the quote).</param>
/// <param name="Image">The drawn rectangle of an image block.</param>
public sealed record BlockGeometry(
    float Y,
    float Height,
    float TextX,
    float TextY,
    IBlockLayout? Text,
    float X = 0f,
    float Width = 0f,
    bool Empty = false,
    LayoutRect? Header = null,
    LayoutRect? Image = null);
