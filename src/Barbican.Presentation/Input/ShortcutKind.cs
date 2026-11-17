// Mirrors: Rust shortcuts.rs:58 Action (the variants). Stub from stage F of M2; owner menus.

namespace Barbican.Presentation.Input;

/// <summary>What a resolved key press means.</summary>
public enum ShortcutKind
{
    /// <summary>Not ours: let the older handler have it.</summary>
    Pass,

    /// <summary>Ours, and nothing to do (the first half of a g sequence).</summary>
    Swallow,
    Palette,
    Help,
    A11y,
    Refresh,
    Compose,
    FocusSearch,
    GoTo,
    ArmGo,
    Open,
    ToggleSelect,
    Extend,
    Mail,
    Undo,
    ShowCc,
    ShowBcc,
    Cal,
    CalView,
    NewEvent,
    EditEvent,
    SaveEvent,
    CloseEditor,
    Move,
    First,
    Last,
    OpenItem,
    Delete,
    Clear,
    Menu,
    Rename,
    Copy,
    Cut,
    Paste,
    Up,
}
