// Mirrors: M2 plan section 4.2 (MenuItemSpec); spec/menus.md item by item; Rust ctxmenu.rs:34-41. Contract from stage F of M2; owner menus.

namespace Barbican.Presentation.Menus;

/// <summary>One row of a menu as data.</summary>
public sealed record MenuItemSpec(
    MenuItemKind Kind,
    string Label,
    string? CommandId = null,
    string? Argument = null,
    string? Hint = null,
    string? Icon = null,
    bool Danger = false,
    string? ConfirmLabel = null,
    bool Enabled = true,
    bool? Checked = null,
    string? SubmenuId = null,
    int Indent = 0);
