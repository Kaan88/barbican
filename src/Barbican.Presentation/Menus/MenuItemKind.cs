// Mirrors: M2 plan section 4.2; Rust ctxmenu.rs:34-41 (it, sub, danger, off, sep, header). Contract from stage F of M2; owner menus.

namespace Barbican.Presentation.Menus;

/// <summary>What a menu row is.</summary>
public enum MenuItemKind
{
    Item,
    Separator,
    Header,
    Submenu,
}
