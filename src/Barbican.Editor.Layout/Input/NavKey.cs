// Mirrors: Rust src/rte/mod.rs handle_key :536 (the movement and deletion keys the layout-aware controller owns).

namespace Barbican.Editor.Layout.Input;

/// <summary>A navigation or deletion key; shortcuts with Ctrl+letter are the Presentation key map's.</summary>
public enum NavKey
{
    Left,
    Right,
    Up,
    Down,
    Home,
    End,
    PageUp,
    PageDown,
    Backspace,
    Delete,
    Enter,
    Tab,
    Escape,
}
