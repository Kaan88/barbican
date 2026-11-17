// Mirrors: M2 plan section 4.2 (IMenuProvider). Owner menus.

namespace Barbican.Presentation.Menus;

/// <summary>A provider that is one function: the menus and flyouts of <see cref="MenuProviders"/>.</summary>
public sealed class DelegateMenuProvider(string menuId, Func<MenuContext, IReadOnlyList<MenuItemSpec>> build) : IMenuProvider
{
    /// <inheritdoc />
    public string MenuId { get; } = menuId;

    /// <inheritdoc />
    public IReadOnlyList<MenuItemSpec> Build(MenuContext context) => build(context);
}
