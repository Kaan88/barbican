// Mirrors: Rust src/rte/layout.rs (tag rust-final; parley there, Topten.RichTextKit here).
// Spike S3, docs/adr/0006-editor-engine.md. No Bulwark/RFC/Stalwart counterpart (Bulwark uses TipTap in a browser).

namespace Barbican.Editor.Layout;

/// <summary>Where one block sits in the document (Rust layout.rs <c>Geo</c>, text blocks only in the spike).</summary>
/// <param name="Y">Top of the block's box.</param>
/// <param name="Height">Height of the box.</param>
/// <param name="TextX">Origin of the text layout.</param>
/// <param name="TextY">Origin of the text layout.</param>
/// <param name="Text">The laid-out text; null for atomic blocks.</param>
public sealed record BlockGeometry(float Y, float Height, float TextX, float TextY, IBlockLayout? Text);
