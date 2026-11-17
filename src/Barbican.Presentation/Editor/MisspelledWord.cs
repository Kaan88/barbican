// Mirrors: M3 plan section 4.5 (IEditorViewModel is the contract; this is what the editor control itself needs beyond it, additive). Rust src/rte/mod.rs on_cmd, ctx_info, source_cmd.

namespace Barbican.Presentation.Editor;

/// <summary>A misspelt word as the context menu needs it.</summary>
public sealed record MisspelledWord(int Block, int Start, int End, string Text);
