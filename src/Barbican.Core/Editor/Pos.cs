// Mirrors: Rust src/rte/model.rs (tag rust-final); Bulwark components/email/email-composer.tsx (TipTap schema).
// No RFC or Stalwart counterpart: the composer document model is client-side only.

using System.Runtime.InteropServices;

namespace Barbican.Core.Editor;

/// <summary>
/// A caret position: block index <see cref="B"/> and offset <see cref="O"/> into the block text.
/// Rust used UTF-8 byte offsets; here the offset is in UTF-16 code units (C# string indices) and
/// never splits a surrogate pair. The text engine counts code points; the view maps between them.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct Pos(int B, int O) : IComparable<Pos>
{
    public int CompareTo(Pos other) => B != other.B ? B.CompareTo(other.B) : O.CompareTo(other.O);

    public static Pos Min(Pos a, Pos b) => a.CompareTo(b) <= 0 ? a : b;

    public static Pos Max(Pos a, Pos b) => a.CompareTo(b) >= 0 ? a : b;

    public static bool operator <(Pos left, Pos right) => left.CompareTo(right) < 0;

    public static bool operator >(Pos left, Pos right) => left.CompareTo(right) > 0;

    public static bool operator <=(Pos left, Pos right) => left.CompareTo(right) <= 0;

    public static bool operator >=(Pos left, Pos right) => left.CompareTo(right) >= 0;
}
