// Source: Bulwark lib/plugin-sandbox/host-api.ts:238-251,1560-1870 (permissions: list settings:read; add, update, remove, reorder settings:write; discover, getCounts,
// refreshCounts email:read; 500-definition cap, label 1-255, palette colours, ownership by `addedBy`, reorder must be a permutation) and lib/__tests__ for the keyword
// API; the writes are checked through the same `emailKeywords` preference the sidebar reads (TagRules.Parse). Barbican fences over FakeJmap.
using Barbican.Core.Mail.Tags;
using Barbican.Core.Plugins.Api.MailTags;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Microsoft.Extensions.Time.Testing;

namespace Barbican.Core.Tests.Plugins.Api;

public sealed class PluginKeywordMethodsTests
{
    private sealed class Readers : IPluginMailReaders
    {
        public Barbican.Core.Mail.IMailService Reader(Barbican.Core.Accounts.IAccountSession session, string? sharedAccountId) => session.Mail;
    }

    private static Task<PluginApiRig> StartAsync(FakePrefs prefs, params string[] permissions) =>
        PluginApiRig.StartAsync(() => PluginKeywordMethods.Create(new PluginTagDefinitions(prefs, prefs), new PluginKeywordCounts(), new Readers(), new PluginMailRateLimiter(new FakeTimeProvider())), permissions);

    private static string[] Ids(FakePrefs prefs) => [.. TagRules.Parse(prefs.Current.Values["emailKeywords"]).Select(d => d.Id)];

    [Fact]
    public async Task List_returns_the_definitions_the_sidebar_reads()
    {
        var prefs = new FakePrefs();
        var rig = await StartAsync(prefs, "settings:read");
        var list = (await rig.CallAsync("keywords.list", "[]")).Value!;
        Assert.Equal(7, list.Count);
        Assert.Equal(["color", "id", "label"], PluginApiRig.Keys(list.Item(0)), StringComparer.Ordinal);
        Assert.Equal("red", list.Item(0).Property("id").StringValue, StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("keywords.list", "[]", "settings:write")]
    [InlineData("keywords.add", """[[{"id":"x","label":"X"}]]""", "settings:read")]
    [InlineData("keywords.update", """[[{"id":"x","label":"X"}]]""", "settings:read")]
    [InlineData("keywords.remove", """[["x"]]""", "settings:read")]
    [InlineData("keywords.reorder", """[["x"]]""", "settings:read")]
    [InlineData("keywords.discover", "[]", "settings:read")]
    [InlineData("keywords.getCounts", "[]", "settings:read")]
    [InlineData("keywords.refreshCounts", "[]", "settings:write")]
    public async Task Each_method_is_denied_without_its_own_permission_and_writes_nothing(string method, string args, string held)
    {
        var prefs = new FakePrefs();
        var rig = await StartAsync(prefs, held);
        var before = rig.F.Server.ApiCalls.Count;
        Assert.Equal(PluginErrorCode.PermissionDenied, (await rig.CallAsync(method, args)).Error);
        Assert.Equal(0, prefs.Puts);
        Assert.Equal(before, rig.F.Server.ApiCalls.Count);
    }

    [Fact]
    public async Task Add_appends_missing_tags_skips_known_ids_and_the_sidebar_setting_changes()
    {
        var prefs = new FakePrefs();
        var rig = await StartAsync(prefs, "settings:write", "settings:read");
        var result = await rig.CallAsync("keywords.add", """[[{"id":"Billing","label":" Billing "},{"id":"red","label":"Again"},{"id":"ops/db","label":"DB","color":"teal-light","visibility":"hide"}]]""");
        Assert.True(result.IsSuccess);
        Assert.Equal(["added", "skipped"], PluginApiRig.Keys(result.Value!), StringComparer.Ordinal);
        var added = result.Value!.Property("added");
        Assert.Equal(2, added.Count);
        Assert.Equal(["addedBy", "color", "id", "label"], PluginApiRig.Keys(added.Item(0)), StringComparer.Ordinal);
        Assert.Equal("Billing", added.Item(0).Property("label").StringValue, StringComparer.Ordinal);
        Assert.Equal("probe", added.Item(0).Property("addedBy").StringValue, StringComparer.Ordinal);
        Assert.Equal("red", result.Value.Property("skipped").Item(0).StringValue, StringComparer.Ordinal);
        Assert.Equal(["red", "orange", "yellow", "green", "blue", "purple", "pink", "Billing", "ops/db"], Ids(prefs), StringComparer.Ordinal);
        Assert.Equal("teal-light", TagRules.Parse(prefs.Current.Values["emailKeywords"])[8].Color, StringComparer.Ordinal);
        var list = (await rig.CallAsync("keywords.list", "[]")).Value!;
        Assert.Equal("hide", list.Item(8).Property("visibility").StringValue, StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("""[[{"id":"a b","label":"A"}]]""")]
    [InlineData("""[[{"id":"","label":"A"}]]""")]
    [InlineData("""[[{"id":"a","label":"  "}]]""")]
    [InlineData("""[[{"id":"a"}]]""")]
    [InlineData("""[[{"id":"a","label":"A","color":"#ff0000"}]]""")]
    [InlineData("""[[{"id":"a","label":"A","color":"chartreuse"}]]""")]
    [InlineData("""[[{"id":"a","label":"A","visibility":"gone"}]]""")]
    [InlineData("""[{"id":"a","label":"A"}]""")]
    [InlineData("""[[1]]""")]
    [InlineData("[]")]
    public async Task Add_with_invalid_definitions_is_refused_and_changes_nothing(string args)
    {
        var prefs = new FakePrefs();
        var rig = await StartAsync(prefs, "settings:write");
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("keywords.add", args)).Error);
        Assert.Equal(0, prefs.Puts);
    }

    [Fact]
    public async Task Add_accepts_500_definitions_and_refuses_501()
    {
        var prefs = new FakePrefs();
        var rig = await StartAsync(prefs, "settings:write");
        string Many(int n) => "[[" + string.Join(',', Enumerable.Range(0, n).Select(i => $$"""{"id":"t{{i}}","label":"T{{i}}"}""")) + "]]";
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("keywords.add", Many(501))).Error);
        Assert.Equal(0, prefs.Puts);
        Assert.Equal(500, (await rig.CallAsync("keywords.add", Many(500))).Value!.Property("added").Count);
    }

    [Fact]
    public async Task Update_and_remove_touch_only_tags_the_calling_add_on_added()
    {
        var prefs = new FakePrefs();
        var rig = await StartAsync(prefs, "settings:write");
        await rig.CallAsync("keywords.add", """[[{"id":"mine","label":"Mine","color":"red"}]]""");
        var updated = await rig.CallAsync("keywords.update", """[[{"id":"MINE","label":"Renamed"},{"id":"blue","label":"Hijack"}]]""");
        Assert.Equal("mine", updated.Value!.Property("updated").Item(0).Property("id").StringValue, StringComparer.Ordinal);
        Assert.Equal("Renamed", updated.Value.Property("updated").Item(0).Property("label").StringValue, StringComparer.Ordinal);
        Assert.Equal("blue", updated.Value.Property("skipped").Item(0).StringValue, StringComparer.Ordinal);
        Assert.Equal("Blue", TagRules.Parse(prefs.Current.Values["emailKeywords"]).Single(d => string.Equals(d.Id, "blue", StringComparison.Ordinal)).Label, StringComparer.Ordinal);
        var removed = await rig.CallAsync("keywords.remove", """[["mine","green","nope"]]""");
        Assert.Equal(["mine"], [removed.Value!.Property("removed").Item(0).StringValue], StringComparer.Ordinal);
        Assert.Equal(2, removed.Value.Property("skipped").Count);
        Assert.DoesNotContain("mine", Ids(prefs), StringComparer.Ordinal);
        Assert.Contains("green", Ids(prefs), StringComparer.Ordinal);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("keywords.update", """[[{"id":"mine"}]]""")).Error);
    }

    [Fact]
    public async Task Another_add_on_cannot_update_or_remove_a_tag_it_did_not_add()
    {
        var prefs = new FakePrefs();
        var rig = await StartAsync(prefs, "settings:write");
        await rig.CallAsync("keywords.add", """[[{"id":"mine","label":"Mine"}]]""");
        var other = rig.BindOther("other", ["settings:write", "settings:read"]);
        // ownership is the package id: a second add-on sees the tag but does not own it
        var update = await rig.CallAsync(other, "keywords.update", """[[{"id":"mine","label":"Taken"}]]""");
        Assert.Equal("mine", update.Value!.Property("skipped").Item(0).StringValue, StringComparer.Ordinal);
        Assert.Equal(0, update.Value.Property("updated").Count);
        var remove = await rig.CallAsync(other, "keywords.remove", """[["mine"]]""");
        Assert.Equal(0, remove.Value!.Property("removed").Count);
        Assert.Contains("mine", Ids(prefs), StringComparer.Ordinal);
        Assert.Equal("Mine", TagRules.Parse(prefs.Current.Values["emailKeywords"]).Single(d => string.Equals(d.Id, "mine", StringComparison.Ordinal)).Label, StringComparer.Ordinal);
        var own = await rig.CallAsync("keywords.update", """[[{"id":"mine","label":"Still mine"}]]""");
        Assert.Equal("probe", own.Value!.Property("updated").Item(0).Property("addedBy").StringValue, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Reorder_must_name_every_tag_once_and_keeps_definitions_verbatim()
    {
        var prefs = new FakePrefs();
        var rig = await StartAsync(prefs, "settings:write");
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("keywords.reorder", """[["red","orange"]]""")).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("keywords.reorder", """[["red","red","yellow","green","blue","purple","pink"]]""")).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("keywords.reorder", """[["pink","purple","blue","green","yellow","orange","RED"],{"caseSensitive":true}]""")).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("keywords.reorder", """[["pink","purple","blue","green","yellow","orange","red"],{"other":1}]""")).Error);
        Assert.Equal(0, prefs.Puts);
        var ok = await rig.CallAsync("keywords.reorder", """[["pink","purple","blue","green","yellow","orange","RED"]]""");
        Assert.True(ok.IsSuccess);
        Assert.Equal(["pink", "purple", "blue", "green", "yellow", "orange", "red"], Ids(prefs), StringComparer.Ordinal);
        Assert.Equal("Pink", ok.Value!.Item(0).Property("label").StringValue, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Counts_are_measured_by_refresh_and_read_by_get_counts()
    {
        var prefs = new FakePrefs();
        var rig = await StartAsync(prefs, "email:read");
        Assert.Empty((await rig.CallAsync("keywords.getCounts", "[]")).Value!.PropertyNames);
        var fresh = await rig.CallAsync("keywords.refreshCounts", "[]");
        Assert.True(fresh.IsSuccess);
        Assert.Equal(7, fresh.Value!.PropertyNames.Count);
        Assert.Equal(["total", "unread"], PluginApiRig.Keys(fresh.Value.Property("red")), StringComparer.Ordinal);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("keywords.getCounts", """[["a b"]]""")).Error);
        var one = (await rig.CallAsync("keywords.getCounts", """[["red","unknown"]]""")).Value!;
        Assert.Equal(["red"], one.PropertyNames, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Refresh_counts_is_limited_to_six_a_minute()
    {
        var prefs = new FakePrefs();
        var rig = await StartAsync(prefs, "email:read");
        for (var i = 0; i < 6; i++) Assert.True((await rig.CallAsync("keywords.refreshCounts", "[]")).IsSuccess);
        Assert.Equal(PluginErrorCode.RateLimited, (await rig.CallAsync("keywords.refreshCounts", "[]")).Error);
    }

    [Fact]
    public async Task Discover_walks_messages_with_the_limit_and_leaves_out_server_set_keywords()
    {
        var rig = await StartAsync(new FakePrefs(), "email:read");
        var scan = (await rig.CallAsync("keywords.discover", """[{"limit":3}]""")).Value!;
        Assert.Equal(["complete", "keywords", "labels", "scanned", "total"], PluginApiRig.Keys(scan).Order(StringComparer.Ordinal), StringComparer.Ordinal);
        Assert.Equal(3, double.Parse(scan.Property("scanned").NumberText, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("keywords.discover", """[{"limit":0}]""")).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("keywords.discover", """[{"limit":25001}]""")).Error);
        Assert.DoesNotContain("$has", scan.Property("keywords").ToJson().GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_stale_call_writes_nothing_and_a_write_that_goes_stale_reports_an_unknown_outcome()
    {
        var prefs = new FakePrefs();
        var rig = await StartAsync(prefs, "settings:write");
        rig.Active.Valid = false;
        Assert.Equal(PluginErrorCode.NoSession, (await rig.CallAsync("keywords.add", """[[{"id":"x","label":"X"}]]""")).Error);
        Assert.Equal(0, prefs.Puts);
        rig.Active.Valid = true;
        prefs.OnRead = () => rig.Active.Valid = false;
        var result = await rig.CallAsync("keywords.add", """[[{"id":"x","label":"X"}]]""");
        Assert.Equal(PluginErrorCode.UnknownOutcome, result.Error);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task A_revoked_add_on_cannot_write()
    {
        var prefs = new FakePrefs();
        var rig = await StartAsync(prefs, "settings:write");
        rig.Auth.Current = PluginApiRig.Grant(["settings:write"], enabled: false);
        Assert.False((await rig.CallAsync("keywords.add", """[[{"id":"x","label":"X"}]]""")).IsSuccess);
        Assert.Equal(0, prefs.Puts);
    }

    [Fact]
    public void The_palette_check_accepts_base_light_and_dark_hues_only()
    {
        Assert.True(PluginKeywordArgs.IsPaletteKey("red"));
        Assert.True(PluginKeywordArgs.IsPaletteKey("gray-dark"));
        Assert.False(PluginKeywordArgs.IsPaletteKey("red-dark-light"));
        Assert.False(PluginKeywordArgs.IsPaletteKey("#aabbcc"));
        Assert.Equal("green", PluginTagDefinitions.SuggestColor("ops/green", new HashSet<string>(StringComparer.Ordinal)), StringComparer.Ordinal);
        Assert.Contains(PluginTagDefinitions.SuggestColor("whatever", new HashSet<string>(StringComparer.Ordinal)), TagRules.Palette, StringComparer.Ordinal);
    }
}
