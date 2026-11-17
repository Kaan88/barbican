// Mirrors: Bulwark components/layout/mailbox-context-menu.tsx:285-306 (the `unified-mailbox` menu: the row's name as a header, then Refresh; "Empty trash in all accounts" only
// for a unified Trash row, which Barbican has not: its unified view is the Inbox row), spec/menus.md UNIFIED-ROW, I-052. Owner unified (m2-close-a).

using Barbican.Presentation.Commands;
using Barbican.Presentation.Menus;
using static Barbican.Presentation.Menus.MenuItems;

namespace Barbican.Presentation.Unified;

/// <summary>UNIFIED-ROW: the right-click menu of the "All inboxes" row.</summary>
public static class UnifiedMenus
{
    /// <summary>The menu: the row's title, then Refresh.</summary>
    public static IReadOnlyList<MenuItemSpec> Row(MenuContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return [Header(UnifiedSectionViewModel.Title), Item(MenuGlyphs.Refresh, "Refresh", CommandIds.RefreshUnified)];
    }
}
