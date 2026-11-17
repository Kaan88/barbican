// Mirrors: Rust src/rte/model.rs (tag rust-final); Bulwark components/email/email-composer.tsx (TipTap schema).
// No RFC or Stalwart counterpart: the composer document model is client-side only.

namespace Barbican.Core.Editor;

/// <summary>The boolean marks a toolbar button or shortcut toggles.</summary>
public enum Mark
{
    Bold,
    Italic,
    Underline,
    Strike,
    Code,
}
