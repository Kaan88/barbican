// Mirrors: Rust src/rte/layout.rs (tag rust-final; parley there, Topten.RichTextKit here).
// Spike S3, docs/adr/0006-editor-engine.md. No Bulwark/RFC/Stalwart counterpart (Bulwark uses TipTap in a browser).

using System.Runtime.InteropServices;

namespace Barbican.Editor.Layout;

[StructLayout(LayoutKind.Auto)]
public readonly record struct LayoutRect(float X, float Y, float Width, float Height);
