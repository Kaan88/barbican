// Mirrors: Rust src/rte/model.rs (tag rust-final); Bulwark components/email/email-composer.tsx (TipTap schema).
// No RFC or Stalwart counterpart: the composer document model is client-side only.

namespace Barbican.Core.Editor;

public static class AlignExtensions
{
    /// <summary>The CSS <c>text-align</c> value the HTML export writes.</summary>
    public static string Css(this Align align) => align switch
    {
        Align.Center => "center",
        Align.Right => "right",
        Align.Justify => "justify",
        _ => "left",
    };
}
