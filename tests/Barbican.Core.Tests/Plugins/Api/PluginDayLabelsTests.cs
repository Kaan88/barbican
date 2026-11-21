// Source: Bulwark lib/plugin-sandbox/day-labels.ts (checkDayLabels, setDayLabels, clearDayLabels, dayLabelsByDate) and host-api.ts:279,2120 (ui.setDayLabels, ui:calendar-action); Barbican fences: a neighbouring permission is denied,
// the labels are the calling add-on's own, stale grants and ended runtimes leave nothing behind, limits and the per-minute budget.
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Calendar;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;

namespace Barbican.Core.Tests.Plugins.Api;

public sealed class PluginDayLabelsTests
{
    private const string Held = "ui:calendar-action";

    private static readonly string Two = """[[{"date":"2026-12-25","text":" Christmas ","title":"Public holiday"},{"date":"2026-12-26","dot":true}]]""";

    [Theory]
    [InlineData("ui:app-page")]
    [InlineData("calendar:read")]
    public async Task SetDayLabels_is_denied_with_a_neighbouring_permission_and_sets_nothing(string other)
    {
        var rig = await PluginNavigationRig.StartAsync([other]);
        Assert.Equal(PluginErrorCode.PermissionDenied, (await rig.CallAsync("ui.setDayLabels", Two)).Error);
        Assert.True(rig.Untouched);
    }

    [Fact]
    public async Task SetDayLabels_registers_the_labels_trimmed_with_the_add_on_name()
    {
        var rig = await PluginNavigationRig.StartAsync([Held]);
        var result = await rig.CallAsync("ui.setDayLabels", Two);
        Assert.True(result.IsSuccess);
        Assert.Equal(PluginValueKind.Undefined, result.Value!.Kind);
        var christmas = Assert.Single(rig.DayLabels.On(new DateOnly(2026, 12, 25)));
        Assert.Equal("Christmas", christmas.Label.Text);
        Assert.Equal("Public holiday", christmas.Label.Title);
        Assert.False(christmas.Label.Dot);
        Assert.Equal("Probe add-on", christmas.PackageName);
        var boxing = Assert.Single(rig.DayLabels.On(new DateOnly(2026, 12, 26)));
        Assert.True(boxing.Label.Dot);
        Assert.Null(boxing.Label.Text);
    }

    [Fact]
    public async Task SetDayLabels_replaces_the_earlier_labels_and_an_empty_list_removes_them()
    {
        var rig = await PluginNavigationRig.StartAsync([Held]);
        Assert.True((await rig.CallAsync("ui.setDayLabels", Two)).IsSuccess);
        var changes = 0;
        rig.DayLabels.Changed += (_, _) => changes++;
        Assert.True((await rig.CallAsync("ui.setDayLabels", """[[{"date":"2027-01-01","text":"New year"}]]""")).IsSuccess);
        Assert.Empty(rig.DayLabels.On(new DateOnly(2026, 12, 25)));
        Assert.Single(rig.DayLabels.On(new DateOnly(2027, 1, 1)));
        Assert.True((await rig.CallAsync("ui.setDayLabels", "[[]]")).IsSuccess);
        Assert.Empty(rig.DayLabels.All);
        Assert.Equal(2, changes);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[5]")]
    [InlineData("""[[{"text":"x"}]]""")]
    [InlineData("""[[{"date":"2026-1-1","text":"x"}]]""")]
    [InlineData("""[[{"date":"2026-13-01","text":"x"}]]""")]
    [InlineData("""[[{"date":"2026-02-31","text":"x"}]]""")]
    [InlineData("""[[{"date":"2026-12-25"}]]""")]
    [InlineData("""[[{"date":"2026-12-25","text":"   "}]]""")]
    [InlineData("""[[{"date":"2026-12-25","text":5}]]""")]
    [InlineData("""[[{"date":"2026-12-25","dot":false}]]""")]
    [InlineData("""[["not an object"]]""")]
    public async Task SetDayLabels_checks_its_argument_and_registers_nothing(string args)
    {
        var rig = await PluginNavigationRig.StartAsync([Held]);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("ui.setDayLabels", args)).Error);
        Assert.True(rig.Untouched);
    }

    [Fact]
    public async Task SetDayLabels_limits_the_text_to_24_characters_the_title_to_120_and_the_list_to_400()
    {
        var rig = await PluginNavigationRig.StartAsync([Held]);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("ui.setDayLabels", $$"""[[{"date":"2026-12-25","text":"{{new string('t', 25)}}"}]]""")).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("ui.setDayLabels", $$"""[[{"date":"2026-12-25","text":"x","title":"{{new string('t', 121)}}"}]]""")).Error);
        Assert.True((await rig.CallAsync("ui.setDayLabels", $$"""[[{"date":"2026-12-25","text":"{{new string('t', 24)}}","title":"{{new string('t', 120)}}"}]]""")).IsSuccess);
        var many = string.Join(",", Enumerable.Range(0, 401).Select(_ => """{"date":"2026-12-25","dot":true}"""));
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("ui.setDayLabels", "[[" + many + "]]")).Error);
        var max = string.Join(",", Enumerable.Range(0, 400).Select(_ => """{"date":"2026-12-25","dot":true}"""));
        Assert.True((await rig.CallAsync("ui.setDayLabels", "[[" + max + "]]")).IsSuccess);
        Assert.Equal(400, rig.DayLabels.All.Count);
    }

    [Fact]
    public async Task One_add_on_cannot_replace_the_labels_of_another()
    {
        var rig = await PluginNavigationRig.StartAsync([Held]);
        var other = rig.Api.BindOther("other-addon", [Held]);
        Assert.True((await rig.CallAsync("ui.setDayLabels", Two)).IsSuccess);
        Assert.True((await rig.Api.CallAsync(other, "ui.setDayLabels", """[[{"date":"2026-12-25","text":"Theirs"}]]""")).IsSuccess);
        Assert.Equal("Probe add-on,other-addon", string.Join(',', rig.DayLabels.On(new DateOnly(2026, 12, 25)).Select(l => l.PackageName)));
        // the other add-on setting an empty list only clears its own labels
        Assert.True((await rig.Api.CallAsync(other, "ui.setDayLabels", "[[]]")).IsSuccess);
        Assert.Equal("Probe add-on", string.Join(',', rig.DayLabels.On(new DateOnly(2026, 12, 25)).Select(l => l.PackageName)));
    }

    [Fact]
    public async Task A_grant_that_goes_stale_sets_nothing()
    {
        var rig = await PluginNavigationRig.StartAsync([Held]);
        Assert.True((await rig.CallAsync("ui.setDayLabels", Two)).IsSuccess);
        rig.Api.Auth.Current = PluginApiRig.Grant([Held], enabled: false);
        Assert.False((await rig.CallAsync("ui.setDayLabels", """[[{"date":"2027-01-01","dot":true}]]""")).IsSuccess);
        Assert.Empty(rig.DayLabels.On(new DateOnly(2027, 1, 1)));
        Assert.Equal(2, rig.DayLabels.All.Count);
    }

    [Fact]
    public async Task SetDayLabels_is_limited_to_20_a_minute_per_add_on()
    {
        var rig = await PluginNavigationRig.StartAsync([Held]);
        for (var i = 0; i < 20; i++) Assert.True((await rig.CallAsync("ui.setDayLabels", Two)).IsSuccess);
        Assert.Equal(PluginErrorCode.RateLimited, (await rig.CallAsync("ui.setDayLabels", Two)).Error);
        rig.Time.Advance(TimeSpan.FromMinutes(1));
        Assert.True((await rig.CallAsync("ui.setDayLabels", Two)).IsSuccess);
    }

    [Fact]
    public async Task An_add_on_whose_runtime_ends_loses_its_labels_and_cannot_set_new_ones()
    {
        var rig = await PluginNavigationRig.StartAsync([Held]);
        Assert.True((await rig.CallAsync("ui.setDayLabels", Two)).IsSuccess);
        var changes = 0;
        rig.DayLabels.Changed += (_, _) => changes++;
        await rig.Api.Binding.DisposeAsync();
        Assert.Empty(rig.DayLabels.All);
        Assert.Equal(1, changes);
        Assert.False((await rig.CallAsync("ui.setDayLabels", Two)).IsSuccess);
        Assert.Empty(rig.DayLabels.All);
    }

    [Fact]
    public async Task A_new_runtime_takes_the_labels_over_and_an_older_one_cannot()
    {
        var registry = new PluginDayLabelRegistry();
        var one = new PluginDayLabel(new DateOnly(2026, 12, 25), "A", null, false);
        var older = PluginRuntimeBinding.Create("auto-api", PluginApiRig.Grant([Held], generation: 1), PluginFrameKind.Background);
        var newer = PluginRuntimeBinding.Create("auto-api", PluginApiRig.Grant([Held], generation: 2), PluginFrameKind.Background);
        Assert.True(registry.Set(older, [one]));
        Assert.True(registry.Set(newer, [one with { Text = "B" }]));
        Assert.Equal("B", Assert.Single(registry.All).Label.Text);
        Assert.False(registry.Set(older, [one]));
        await older.DisposeAsync();
        Assert.Single(registry.All);
        await newer.DisposeAsync();
        Assert.Empty(registry.All);
    }
}
