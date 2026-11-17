// Mirrors: M2 plan section 4.2 (IContextMenuService); ADR 0005 and 0007 (in-window, constrained popups only). Contract from stage F of M2; owner menus.

namespace Barbican.Presentation.Menus;

/// <summary>Opens and closes the in-window menu.</summary>
public interface IContextMenuService
{
    void Show(MenuContext context, MenuAnchor anchor);

    void Close();

    bool IsOpen { get; }
}
