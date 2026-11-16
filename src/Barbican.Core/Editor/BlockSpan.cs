// Mirrors: Rust src/rte/model.rs (tag rust-final); Bulwark components/email/email-composer.tsx (TipTap schema).
// No RFC or Stalwart counterpart: the composer document model is client-side only.

using System.Runtime.InteropServices;

namespace Barbican.Core.Editor;

/// <summary>A half-open range of block indices [Start, End). Rust used <c>Range&lt;usize&gt;</c>.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct BlockSpan(int Start, int End)
{
    public int Count => End - Start;
}
