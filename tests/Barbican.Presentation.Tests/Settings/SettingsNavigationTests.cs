// The navigation seam both UI streams share (M6 plan section 4.1): green in the plan commit. Frozen; owner integrator.

using Barbican.Presentation.Settings;

namespace Barbican.Presentation.Tests.Settings;

public sealed class SettingsNavigationTests
{
    [Fact]
    public void Open_changes_the_page_and_keeps_a_history()
    {
        var nav = new SettingsNavigation();
        var changes = 0;
        nav.Changed += (_, _) => changes++;
        Assert.Equal(SettingsPageIds.Account, nav.Current);
        nav.Open(SettingsPageIds.Identities);
        nav.Open(SettingsPageIds.Filters, "rule-1");
        Assert.Equal(SettingsPageIds.Filters, nav.Current);
        Assert.Equal("rule-1", nav.Anchor);
        Assert.True(nav.Back());
        Assert.Equal(SettingsPageIds.Identities, nav.Current);
        Assert.True(nav.CanGoForward);
        nav.Open(SettingsPageIds.Vacation);
        Assert.False(nav.CanGoForward);
        Assert.Equal(4, changes);
    }

    [Fact]
    public void An_unknown_page_is_ignored_and_the_same_page_only_changes_the_anchor()
    {
        var nav = new SettingsNavigation();
        var changes = 0;
        nav.Changed += (_, _) => changes++;
        nav.Open("nope");
        nav.Open(SettingsPageIds.Account);
        Assert.Equal(0, changes);
        nav.Open(SettingsPageIds.Account, "storage");
        Assert.Equal("storage", nav.Anchor);
        Assert.False(nav.CanGoBack);
        Assert.Equal(1, changes);
        Assert.False(nav.Back());
    }

    [Fact]
    public void Every_page_id_has_one_entry_and_every_entry_one_owner()
    {
        var ids = typeof(SettingsPageIds).GetFields().Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).ToList();
        Assert.Equal(ids.Order(StringComparer.Ordinal), SettingsNav.Pages.Select(p => p.Id).Order(StringComparer.Ordinal));
        Assert.Equal(SettingsNav.Pages.Count, SettingsNav.Pages.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(SettingsPageOwner.SrvUi, SettingsNav.Find(SettingsPageIds.Filters)!.Owner);
        Assert.Equal(SettingsPageOwner.Plugins, SettingsNav.Find(SettingsPageIds.Addons)!.Owner);
    }
}
