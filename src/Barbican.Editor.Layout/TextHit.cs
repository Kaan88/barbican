// Mirrors: Rust src/rte/layout.rs (tag rust-final; parley there, Topten.RichTextKit here).
// Spike S3, docs/adr/0006-editor-engine.md. No Bulwark/RFC/Stalwart counterpart (Bulwark uses TipTap in a browser).

using System.Runtime.InteropServices;

namespace Barbican.Editor.Layout;

/// <summary>
/// A hit-test result inside one block: a UTF-16 offset into the block text, and whether the caret
/// belongs at the end of the previous line (<see cref="Upstream"/>) when the offset is a soft wrap.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct TextHit(int Offset, bool Upstream);
