// Mirrors: spec/behaviours/shortcuts.md (B-KEY); Rust shortcuts.rs (Slint key text with shift applied, ctrl, alt); Bulwark hooks/use-keyboard-shortcuts.ts.
// Owner mailcore.

namespace Barbican.Presentation.Input;

/// <summary>A key with its modifiers, as the shortcut table spells it (<c>j</c>, <c>Ctrl+K</c>, <c>Shift+G</c>, <c>Escape</c>).</summary>
/// <param name="Key">A printable character (<c>j</c>, <c>/</c>, <c>G</c>) or a name (<c>Enter</c>, <c>Escape</c>, <c>ArrowDown</c> or <c>Down</c>, <c>Home</c>, <c>End</c>, <c>Delete</c>).</param>
/// <param name="Control">Ctrl is held.</param>
/// <param name="Shift">Shift is held (a capital letter in <paramref name="Key"/> also counts).</param>
/// <param name="Alt">Alt is held (no mail key uses it; such chords are not ours).</param>
public readonly record struct KeyChord(string Key, bool Control = false, bool Shift = false, bool Alt = false);
