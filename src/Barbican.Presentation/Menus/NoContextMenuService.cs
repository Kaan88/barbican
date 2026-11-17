// Mirrors: M2 plan section 3.3 (replaceable defaults: every seam has a behaviour-preserving default; menus replaces this one). Stage F of M2.

namespace Barbican.Presentation.Menus;

/// <summary>The default until the menu host exists: nothing opens.</summary>
public sealed class NoContextMenuService : IContextMenuService
{
    /// <inheritdoc />
    public bool IsOpen => false;

    /// <inheritdoc />
    public void Show(MenuContext context, MenuAnchor anchor)
    {
    }

    /// <inheritdoc />
    public void Close()
    {
    }
}
