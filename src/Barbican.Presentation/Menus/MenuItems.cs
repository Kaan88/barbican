// Mirrors: Rust ctxmenu.rs it/sub/danger/off/sep/header :34-:41. Owner menus.

namespace Barbican.Presentation.Menus;

/// <summary>Builders for <see cref="MenuItemSpec"/> rows, named as Rust's item builders.</summary>
public static class MenuItems
{
    public static MenuItemSpec Item(string icon, string label, string commandId, string? hint = null, string? argument = null, bool enabled = true, bool? check = null, int indent = 0) =>
        new(MenuItemKind.Item, label, commandId, argument, hint, icon, Enabled: enabled, Checked: check, Indent: indent);

    public static MenuItemSpec Danger(string icon, string label, string commandId, string? hint = null, string? confirm = null, string? argument = null, bool enabled = true) =>
        new(MenuItemKind.Item, label, commandId, argument, hint, icon, Danger: true, ConfirmLabel: confirm, Enabled: enabled);

    public static MenuItemSpec Sub(string icon, string label, string submenuId, bool enabled = true) =>
        new(MenuItemKind.Submenu, label, Icon: icon, SubmenuId: submenuId, Enabled: enabled);

    /// <summary>A disabled placeholder row ("No tags").</summary>
    public static MenuItemSpec Note(string label) => new(MenuItemKind.Item, label, Enabled: false);

    public static MenuItemSpec Sep() => new(MenuItemKind.Separator, string.Empty);

    public static MenuItemSpec Header(string label) => new(MenuItemKind.Header, label);
}
