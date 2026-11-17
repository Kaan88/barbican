// Mirrors: Rust src/rte/editor.rs (tag rust-final); Bulwark components/email/email-composer.tsx (TipTap commands and input rules). No RFC or Stalwart counterpart. Contract from the M3 plan (stream editor-core).

namespace Barbican.Core.Editor;

/// <summary>What an edit is, for undo grouping: typing and deleting merge into one step within 1200 ms (Rust <c>EditKind</c>).</summary>
public enum EditKind
{
    Typing,
    Delete,
    Other,
}
