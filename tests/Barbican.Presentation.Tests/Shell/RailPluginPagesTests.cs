// Source: Bulwark components/layout/navigation-rail.tsx:459-512,700-720 (add-on pages after the built-in apps, active while their route is open, icon with the puzzle piece as fallback, tooltip "title (add-on)"),
// lib/plugin-sandbox/pages.ts (the registry), and the shell state a page click or ui.openPage sets.
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Pages;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Runtime;
using Barbican.Presentation.Shell;
using Barbican.Presentation.Tests.Support;
using Barbican.Presentation.Theme;

namespace Barbican.Presentation.Tests.Shell;

public sealed class RailPluginPagesTests
{
    private static PluginRuntimeBinding Binding(string id, long generation = 1) =>
        PluginRuntimeBinding.Create("auto-api", new PluginGrantSnapshot(new PluginPackageIdentity(id, "1.0.0", new string('a', 64)), generation, true, ["ui:app-page"], ["a1"]), PluginFrameKind.Background);

    private static string J(params string[] keys) => string.Join(" ", keys);

    private static PluginPage Page(string id, string? icon = null) => new(id, id.ToUpperInvariant(), icon);

    [Fact]
    public void The_rail_lists_the_pages_of_the_add_ons_in_registry_order_after_the_apps()
    {
        using var h = new ListHarness();
        var registry = new PluginPageRegistry(id => id + " add-on");
        using var rail = new RailViewModel(h.Shell, h.Sidebar, h.Active, registry);
        Assert.Empty(rail.PluginPages);
        Assert.True(registry.Set(Binding("one"), [Page("a", "Star"), Page("b")]));
        Assert.True(registry.Set(Binding("two"), [Page("c", "NoSuchIcon")]));
        Assert.Equal(J("one/a", "one/b", "two/c"), string.Join(" ", rail.PluginPages.Select(p => p.Key)));
        Assert.Equal("A (one add-on)", rail.PluginPages[0].Tooltip);
        Assert.Equal(J("Star", "Puzzle", "Puzzle"), string.Join(" ", rail.PluginPages.Select(p => p.IconKind)));
        Assert.Equal(5, rail.Apps.Count);
    }

    [Fact]
    public void Every_icon_the_rail_can_pick_exists_in_the_icon_set()
    {
        foreach (var name in new[] { "Mail", "calendar", "Users", "wrench", "Book-Open", "FileText", "Globe", "puzzle", "sparkles", null, "unknown" })
        {
            Assert.True(Tokens.Icons.ContainsKey(PluginPageIcons.KindFor(name)), name);
        }

        Assert.Equal("Puzzle", PluginPageIcons.KindFor(null));
        Assert.Equal("Cal", PluginPageIcons.KindFor("Calendar"));
        Assert.Equal("Book", PluginPageIcons.KindFor("book-open"));
    }

    [Fact]
    public void Clicking_an_entry_opens_its_page_and_marks_only_it_active_and_an_app_leaves_it()
    {
        using var h = new ListHarness();
        var registry = new PluginPageRegistry();
        using var rail = new RailViewModel(h.Shell, h.Sidebar, h.Active, registry);
        Assert.True(registry.Set(Binding("one"), [Page("a"), Page("b")]));
        Assert.False(rail.HasActivePluginPage);
        rail.OpenPluginPage(rail.PluginPages[1]);
        Assert.Equal("one/b", h.Shell.ActivePluginPage);
        Assert.True(rail.HasActivePluginPage);
        Assert.Equal("False True", string.Join(" ", rail.PluginPages.Select(p => p.IsActive)));
        rail.OpenPluginPage(rail.PluginPages[0]);
        Assert.Equal("True False", string.Join(" ", rail.PluginPages.Select(p => p.IsActive)));
        // choosing an app of the rail leaves the page, even the app that was current before it
        Assert.True(h.Shell.TryActivate(AppId.Mail));
        Assert.Null(h.Shell.ActivePluginPage);
        Assert.False(rail.HasActivePluginPage);
        Assert.All(rail.PluginPages, p => Assert.False(p.IsActive));
    }

    [Fact]
    public async Task A_disabled_or_removed_add_on_loses_its_entries_and_its_open_page()
    {
        using var h = new ListHarness();
        var registry = new PluginPageRegistry();
        using var rail = new RailViewModel(h.Shell, h.Sidebar, h.Active, registry);
        var one = Binding("one");
        Assert.True(registry.Set(one, [Page("a")]));
        Assert.True(registry.Set(Binding("two"), [Page("c")]));
        h.Shell.OpenPluginPage("one/a", "deep/link");
        Assert.Equal("deep/link", h.Shell.PluginPagePath);
        await one.DisposeAsync();
        Assert.Equal(J("two/c"), string.Join(" ", rail.PluginPages.Select(p => p.Key)));
        Assert.Null(h.Shell.ActivePluginPage);
        Assert.Equal(string.Empty, h.Shell.PluginPagePath);
    }

    [Fact]
    public void A_changed_title_replaces_the_entry_and_the_order_follows_the_registry()
    {
        using var h = new ListHarness();
        var registry = new PluginPageRegistry();
        using var rail = new RailViewModel(h.Shell, h.Sidebar, h.Active, registry);
        var one = Binding("one");
        Assert.True(registry.Set(one, [Page("a"), Page("b")]));
        Assert.True(registry.Set(one, [new PluginPage("b", "Renamed", null), Page("a")]));
        Assert.Equal(J("one/b", "one/a"), string.Join(" ", rail.PluginPages.Select(p => p.Key)));
        Assert.Equal("Renamed", rail.PluginPages[0].Title);
    }

    [Fact]
    public void Opening_the_same_page_again_only_changes_its_path()
    {
        using var h = new ListHarness();
        var changed = new List<string?>();
        h.Shell.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        h.Shell.OpenPluginPage("one/a", string.Empty);
        h.Shell.OpenPluginPage("one/a", "x/y");
        Assert.Equal("x/y", h.Shell.PluginPagePath);
        Assert.Equal(1, changed.Count(n => string.Equals(n, nameof(ShellViewModel.ActivePluginPage), StringComparison.Ordinal)));
    }
}
