// Mirrors: Rust src/rte/layout.rs (tag rust-final; parley there, Topten.RichTextKit here).
// Spike S3, docs/adr/0006-editor-engine.md. No Bulwark/RFC/Stalwart counterpart (Bulwark uses TipTap in a browser).

using Barbican.Core.Editor;

namespace Barbican.Editor.Layout;

/// <summary>
/// Lays out one block's runs. One implementation today (<see cref="RichTextKitEngine"/>, both
/// heads); PORT-PLAN.md §3.8 keeps a DirectWrite/Win2D backend for the Windows head possible
/// behind this seam if the S3/M3 text-quality gate ever fails.
/// </summary>
public interface ITextLayoutEngine
{
    IBlockLayout Layout(Block block, float width, LayoutStyle style);
}
