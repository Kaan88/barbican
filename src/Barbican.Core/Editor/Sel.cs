// Mirrors: Rust src/rte/model.rs (tag rust-final); Bulwark components/email/email-composer.tsx (TipTap schema).
// No RFC or Stalwart counterpart: the composer document model is client-side only.

using System.Runtime.InteropServices;

namespace Barbican.Core.Editor;

/// <summary>A selection: <see cref="Anchor"/> stays put, <see cref="Focus"/> moves with the caret.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct Sel(Pos Anchor, Pos Focus)
{
    public bool IsCollapsed => Anchor == Focus;

    public Pos Start => Pos.Min(Anchor, Focus);

    public Pos End => Pos.Max(Anchor, Focus);

    public static Sel Caret(Pos p) => new(p, p);
}
