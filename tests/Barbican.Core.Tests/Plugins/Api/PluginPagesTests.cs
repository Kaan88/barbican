// Source: Bulwark lib/plugin-sandbox/pages.ts (checkPages, setPluginPages, findPage), page-navigation.ts (pagePathHref, requestPageNavigation), host-api.ts:284-286,2123-2124 (ui.setPages, ui.openPage, ui:app-page) and the
// lib/__tests__ page tests; Barbican fences: a neighbouring permission is denied, the pages are the calling add-on's own, stale grants and ended runtimes leave nothing behind, limits and the per-minute budget.
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Pages;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;

namespace Barbican.Core.Tests.Plugins.Api;

public sealed class PluginPagesTests
{
    private const string Held = "ui:app-page";

    private static string J(params string[] keys) => string.Join(" ", keys);

    private static string Keys(PluginPageRegistry registry) => string.Join(" ", registry.All.Select(p => p.Key));

    private static readonly string Two = """[[{"id":"inbox-zero","title":"Inbox zero","icon":"Star"},{"id":"stats","title":"Stats"}]]""";

    [Theory]
    [InlineData("ui.setPages", "[[]]")]
    [InlineData("ui.openPage", """["x"]""")]
    public async Task Both_methods_are_denied_with_a_neighbouring_permission_and_change_nothing(string method, string args)
    {
        var rig = await PluginNavigationRig.StartAsync(["ui:sidebar-widget", "ui:tasks"]);
        Assert.Equal(PluginErrorCode.PermissionDenied, (await rig.CallAsync(method, args)).Error);
        Assert.True(rig.Untouched);
    }

    [Fact]
    public async Task SetPages_registers_the_pages_of_the_calling_add_on_with_its_name_in_order()
    {
        var rig = await PluginNavigationRig.StartAsync([Held]);
        var result = await rig.CallAsync("ui.setPages", Two);
        Assert.True(result.IsSuccess);
        Assert.Equal(PluginValueKind.Undefined, result.Value!.Kind);
        var all = rig.Pages.All;
        Assert.Equal(J("probe/inbox-zero", "probe/stats"), Keys(rig.Pages));
        Assert.Equal("Probe add-on", all[0].PackageName);
        Assert.Equal("Inbox zero (Probe add-on)", all[0].Tooltip);
        Assert.Equal("Star", all[0].Page.Icon);
        Assert.Null(all[1].Page.Icon);
    }

    [Fact]
    public async Task SetPages_replaces_the_list_trims_the_title_and_an_empty_list_removes_the_entries()
    {
        var rig = await PluginNavigationRig.StartAsync([Held]);
        Assert.True((await rig.CallAsync("ui.setPages", Two)).IsSuccess);
        var changes = 0;
        rig.Pages.Changed += (_, _) => changes++;
        Assert.True((await rig.CallAsync("ui.setPages", """[[{"id":"only","title":"  Only  "}]]""")).IsSuccess);
        Assert.Equal("Only", Assert.Single(rig.Pages.All).Page.Title);
        Assert.True((await rig.CallAsync("ui.setPages", "[[]]")).IsSuccess);
        Assert.Empty(rig.Pages.All);
        Assert.Equal(2, changes);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("""[{"id":"a","title":"A"}]""")]
    [InlineData("""[[{"title":"A"}]]""")]
    [InlineData("""[[{"id":"a"}]]""")]
    [InlineData("""[[{"id":"A","title":"A"}]]""")]
    [InlineData("""[[{"id":"-a","title":"A"}]]""")]
    [InlineData("""[[{"id":"a b","title":"A"}]]""")]
    [InlineData("""[[{"id":"a","title":"   "}]]""")]
    [InlineData("""[[{"id":"a","title":"A","icon":"no spaces"}]]""")]
    [InlineData("""[[{"id":"a","title":"A","icon":5}]]""")]
    [InlineData("""[[{"id":"a","title":"A","extra":1}]]""")]
    [InlineData("""[[{"id":"a","title":"A"},{"id":"a","title":"B"}]]""")]
    [InlineData("""[[{"id":"a","title":"A"},{"id":"b","title":"B"},{"id":"c","title":"C"},{"id":"d","title":"D"}]]""")]
    [InlineData("""[[{"id":"abcdefghijklmnopqrstuvwxyz0123456789","title":"A"}]]""")]
    [InlineData("""[[{"id":"a","title":"0123456789012345678901234567890123456789X"}]]""")]
    public async Task SetPages_checks_its_argument_and_registers_nothing(string args)
    {
        var rig = await PluginNavigationRig.StartAsync([Held]);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("ui.setPages", args)).Error);
        Assert.True(rig.Untouched);
    }

    [Fact]
    public async Task SetPages_accepts_three_pages_and_a_title_of_40_characters()
    {
        var rig = await PluginNavigationRig.StartAsync([Held]);
        var title = new string('t', 40);
        var args = $$"""[[{"id":"a","title":"{{title}}"},{"id":"b","title":"B"},{"id":"c","title":"C"}]]""";
        Assert.True((await rig.CallAsync("ui.setPages", args)).IsSuccess);
        Assert.Equal(3, rig.Pages.All.Count);
    }

    [Fact]
    public async Task One_add_on_cannot_replace_or_see_the_pages_of_another()
    {
        var rig = await PluginNavigationRig.StartAsync([Held]);
        var other = rig.Api.BindOther("other-addon", [Held]);
        Assert.True((await rig.CallAsync("ui.setPages", Two)).IsSuccess);
        Assert.True((await rig.Api.CallAsync(other, "ui.setPages", """[[{"id":"theirs","title":"Theirs"}]]""")).IsSuccess);
        Assert.Equal(J("probe/inbox-zero", "probe/stats", "other-addon/theirs"), Keys(rig.Pages));
        // the other add-on setting an empty list only clears its own entries
        Assert.True((await rig.Api.CallAsync(other, "ui.setPages", "[[]]")).IsSuccess);
        Assert.Equal(J("probe/inbox-zero", "probe/stats"), Keys(rig.Pages));
        // and it cannot open a page it does not own
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.Api.CallAsync(other, "ui.openPage", """["stats"]""")).Error);
        Assert.Empty(rig.PageNav.Opened);
    }

    [Fact]
    public async Task OpenPage_moves_to_an_own_page_with_the_path_below_it()
    {
        var rig = await PluginNavigationRig.StartAsync([Held]);
        Assert.True((await rig.CallAsync("ui.setPages", Two)).IsSuccess);
        var result = await rig.CallAsync("ui.openPage", """["stats","/reports/2026 q4/a"]""");
        Assert.True(result.Value!.BooleanValue);
        Assert.Equal(("probe", "stats", "reports/2026 q4/a"), Assert.Single(rig.PageNav.Opened));
        Assert.True((await rig.CallAsync("ui.openPage", """["inbox-zero"]""")).Value!.BooleanValue);
        Assert.Equal(("probe", "inbox-zero", ""), rig.PageNav.Opened[1]);
    }

    [Fact]
    public async Task OpenPage_answers_false_when_the_app_cannot_take_the_move()
    {
        var rig = await PluginNavigationRig.StartAsync([Held]);
        Assert.True((await rig.CallAsync("ui.setPages", Two)).IsSuccess);
        rig.PageNav.Answer = false;
        Assert.False((await rig.CallAsync("ui.openPage", """["stats"]""")).Value!.BooleanValue);
    }

    [Theory]
    [InlineData("""["never-set"]""")]
    [InlineData("[]")]
    [InlineData("[5]")]
    [InlineData("""["Stats"]""")]
    [InlineData("""["stats",5]""")]
    [InlineData("""["stats","../x"]""")]
    [InlineData("""["stats","a/./b"]""")]
    [InlineData("""["stats","a/../b"]""")]
    [InlineData("""["stats","x","y"]""")]
    public async Task OpenPage_refuses_a_page_the_add_on_never_set_and_a_bad_path(string args)
    {
        var rig = await PluginNavigationRig.StartAsync([Held]);
        Assert.True((await rig.CallAsync("ui.setPages", Two)).IsSuccess);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("ui.openPage", args)).Error);
        Assert.Empty(rig.PageNav.Opened);
    }

    [Fact]
    public async Task OpenPage_refuses_a_path_longer_than_2000_characters_and_takes_one_of_2000()
    {
        var rig = await PluginNavigationRig.StartAsync([Held]);
        Assert.True((await rig.CallAsync("ui.setPages", Two)).IsSuccess);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("ui.openPage", $$"""["stats","{{new string('p', 2001)}}"]""")).Error);
        Assert.True((await rig.CallAsync("ui.openPage", $$"""["stats","{{new string('p', 2000)}}"]""")).Value!.BooleanValue);
    }

    [Fact]
    public async Task A_grant_that_goes_stale_registers_and_opens_nothing()
    {
        var rig = await PluginNavigationRig.StartAsync([Held]);
        Assert.True((await rig.CallAsync("ui.setPages", Two)).IsSuccess);
        rig.Api.Auth.Current = PluginApiRig.Grant([Held], enabled: false);
        Assert.False((await rig.CallAsync("ui.setPages", """[[{"id":"late","title":"Late"}]]""")).IsSuccess);
        Assert.False((await rig.CallAsync("ui.openPage", """["stats"]""")).IsSuccess);
        Assert.Equal(J("probe/inbox-zero", "probe/stats"), Keys(rig.Pages));
        Assert.Empty(rig.PageNav.Opened);
    }

    [Fact]
    public async Task Both_methods_are_limited_to_20_a_minute_per_add_on()
    {
        var rig = await PluginNavigationRig.StartAsync([Held]);
        for (var i = 0; i < 20; i++) Assert.True((await rig.CallAsync("ui.setPages", Two)).IsSuccess);
        Assert.Equal(PluginErrorCode.RateLimited, (await rig.CallAsync("ui.setPages", Two)).Error);
        for (var i = 0; i < 20; i++) Assert.True((await rig.CallAsync("ui.openPage", """["stats"]""")).IsSuccess);
        Assert.Equal(PluginErrorCode.RateLimited, (await rig.CallAsync("ui.openPage", """["stats"]""")).Error);
        Assert.Equal(20, rig.PageNav.Opened.Count);
        rig.Time.Advance(TimeSpan.FromMinutes(1));
        Assert.True((await rig.CallAsync("ui.setPages", Two)).IsSuccess);
    }

    [Fact]
    public async Task An_add_on_whose_runtime_ends_loses_its_pages_and_cannot_set_new_ones()
    {
        var rig = await PluginNavigationRig.StartAsync([Held]);
        Assert.True((await rig.CallAsync("ui.setPages", Two)).IsSuccess);
        var changes = 0;
        rig.Pages.Changed += (_, _) => changes++;
        await rig.Api.Binding.DisposeAsync();
        Assert.Empty(rig.Pages.All);
        Assert.Equal(1, changes);
        Assert.False((await rig.CallAsync("ui.setPages", Two)).IsSuccess);
        Assert.Empty(rig.Pages.All);
        Assert.Null(rig.Pages.Find("probe", "stats"));
    }

    [Fact]
    public async Task A_new_runtime_of_the_same_add_on_takes_the_entries_over_and_an_older_one_cannot()
    {
        var registry = new PluginPageRegistry();
        var one = new PluginPage("a", "A", null);
        var older = PluginRuntimeBinding.Create("auto-api", PluginApiRig.Grant([Held], generation: 1), PluginFrameKind.Background);
        var newer = PluginRuntimeBinding.Create("auto-api", PluginApiRig.Grant([Held], generation: 2), PluginFrameKind.Background);
        Assert.True(registry.Set(older, [one]));
        Assert.True(registry.Set(newer, [new PluginPage("b", "B", null)]));
        Assert.Equal(J("probe/b"), Keys(registry));
        Assert.False(registry.Set(older, [one]));
        // the replaced runtime ending does not take the new runtime's entries with it
        await older.DisposeAsync();
        Assert.Equal(J("probe/b"), Keys(registry));
        await newer.DisposeAsync();
        Assert.Empty(registry.All);
    }

    [Fact]
    public void The_registry_never_holds_more_than_three_pages_for_an_add_on()
    {
        var registry = new PluginPageRegistry();
        var binding = PluginRuntimeBinding.Create("auto-api", PluginApiRig.Grant([Held]), PluginFrameKind.Background);
        var four = Enumerable.Range(0, 4).Select(i => new PluginPage("p" + i, "P", null)).ToArray();
        Assert.False(registry.Set(binding, four));
        Assert.Empty(registry.All);
    }
}
