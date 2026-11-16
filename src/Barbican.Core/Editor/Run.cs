// Mirrors: Rust src/rte/model.rs (tag rust-final); Bulwark components/email/email-composer.tsx (TipTap schema).
// No RFC or Stalwart counterpart: the composer document model is client-side only.

namespace Barbican.Core.Editor;

/// <summary>A piece of text with one set of marks. Offsets into it are UTF-16 code units.</summary>
public sealed record Run(string Text, Marks Marks);
