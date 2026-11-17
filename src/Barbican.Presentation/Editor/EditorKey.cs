// Mirrors: Rust src/rte/mod.rs on_key :517 (text, ctrl, shift, alt). The App turns a WinUI key event into this; the map below is a pure table.

namespace Barbican.Presentation.Editor;

/// <summary>A key with its modifiers; <see cref="Name"/> is a letter or digit upper-case ("B", "7"), a symbol ("\\", "[", "]", " ") or a key name ("Left", "Enter", "Tab", "Escape", "Delete", "Backspace", "Insert").</summary>
public readonly record struct EditorKey(string Name, bool Ctrl = false, bool Shift = false, bool Alt = false);
