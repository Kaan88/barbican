// New tests: UNIFIED-ROW (spec/menus.md; Bulwark mailbox-context-menu.tsx:285-306): the row's title as a header, then Refresh. spec I-052. Owner unified (m2-close-a).

#pragma warning disable MA0002, MA0006 // asserts compare labels ordinally by nature

using Barbican.Presentation.Commands;
using Barbican.Presentation.Menus;
using Barbican.Presentation.Unified;
using static Barbican.Presentation.Tests.Menus.MenuKit;

namespace Barbican.Presentation.Tests.Unified;

public sealed class UnifiedMenusTests
{
    [Fact]
    public void The_unified_row_menu_is_the_title_and_refresh()
    {
        var rows = UnifiedMenus.Row(Context(MenuIds.UnifiedRow));
        Assert.Equal(["# All inboxes", "Refresh"], Labels(rows));
        Assert.Equal(CommandIds.RefreshUnified, rows[1].CommandId);
        Assert.True(rows[1].Enabled);
    }
}
