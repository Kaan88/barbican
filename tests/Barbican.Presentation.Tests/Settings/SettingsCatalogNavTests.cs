// The catalog's pages and the navigation's pages are the same set (every schema tab has a page entry, so the screen can show it and the search can open it). M6 stream set-data; the frozen navigation is the plan commit's.

using Barbican.Core.Settings.Pages;
using Barbican.Presentation.Settings;

namespace Barbican.Presentation.Tests.Settings;

public sealed class SettingsCatalogNavTests
{
    [Fact]
    public void Every_catalog_page_has_a_navigation_entry_of_the_same_group()
    {
        foreach (var page in SettingsCatalog.Pages())
        {
            var nav = SettingsNav.Find(page.Id);
            Assert.NotNull(nav);
            Assert.Equal(page.Group, nav.Group);
        }
    }
}
