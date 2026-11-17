// Mirrors: spec/behaviours/shortcuts.md (B-KEY); Rust key handling in ui/app.slint. Contract stub from F2; owner mailcore.

namespace Barbican.Presentation.Input;

/// <summary>A key with its modifiers, as the shortcut table spells it (<c>j</c>, <c>Ctrl+K</c>, <c>Shift+G</c>, <c>Escape</c>).</summary>
public readonly record struct KeyChord(string Key, bool Control = false, bool Shift = false, bool Alt = false);
