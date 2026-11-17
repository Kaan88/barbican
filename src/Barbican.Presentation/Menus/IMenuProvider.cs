// Mirrors: M2 plan section 4.2 (IMenuProvider; multi-registration: menus, reader and unified each provide some); spec/menus.md. Contract from stage F of M2; owner menus.

namespace Barbican.Presentation.Menus;

/// <summary>Builds the rows of one menu for a context (enable state evaluated now).</summary>
public interface IMenuProvider
{
    /// <summary>A <see cref="MenuIds"/> constant.</summary>
    string MenuId { get; }

    IReadOnlyList<MenuItemSpec> Build(MenuContext context);
}
