// Source: Bulwark lib/plugin-sandbox/host-api.ts:204-206 (app.searchSettings no static gate, app.getSettings settings:read), assistant/c.ts:33-36 (app.describeSettings settings:read, app.setSettings and app.undoSettings settings:change),
// assistant/c.settings.ts:1-300 (the allow-list, the checks, write-and-read-back, the undo token), lib/settings-schema.ts (the allow-list, the DENIED list, the one-way safety nets), host-api-app.ts:63-182 (search, getSettings).
// The real PluginApiDispatcher over the real SettingValues (Coerce, onBeforeSettingChange, prefs, Changed) on an in-memory prefs store; Barbican fences: denial with a neighbouring permission touches no setting, a stale session
// before a write writes nothing, one during it reports an unknown outcome, 30 writes and 60 reads a minute.
#pragma warning disable MA0006, MA0002 // assertions compare string literals

using System.Text.Json.Nodes;
using Barbican.Core.Plugins.Api.App;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Settings.Pages;
using Microsoft.Extensions.Time.Testing;

namespace Barbican.Core.Tests.Plugins.Api;

public sealed class PluginAppSettingsTests
{
    private sealed class Rig
    {
        public PluginApiRig Api { get; private set; } = null!;

        public FakePrefs Prefs { get; } = new();

        public HookBus Hooks { get; } = new(new FakeTimeProvider());

        public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 21, 9, 0, 0, TimeSpan.Zero));

        public SettingValues Values { get; private set; } = null!;

        public List<string> Changed { get; } = [];

        public static async Task<Rig> StartAsync(params string[] permissions)
        {
            var rig = new Rig();
            rig.Values = new SettingValues(rig.Prefs, rig.Prefs, rig.Hooks);
            rig.Values.Changed += (_, e) => rig.Changed.Add(e.Key);
            rig.Api = await PluginApiRig.StartAsync(() => PluginAppSettingsMethods.Create(new PluginSettingsHost(rig.Values, new PluginMailRateLimiter(rig.Time), rig.Time)), permissions);
            return rig;
        }

        public Task<PluginCallOutcome> CallAsync(string method, string args) => Api.CallAsync(method, args);

        public string? Stored(string key) => (string?)Prefs.Current.Values[key];
    }

    [Theory]
    [InlineData("app.getSettings", "[]", "settings:change")]
    [InlineData("app.describeSettings", """[["density"]]""", "settings:change")]
    [InlineData("app.setSettings", """[{"density":"compact"}]""", "settings:read")]
    [InlineData("app.undoSettings", """["abc"]""", "settings:read")]
    public async Task A_method_is_denied_with_a_neighbouring_permission_and_changes_nothing(string method, string args, string held)
    {
        var rig = await Rig.StartAsync(held);
        Assert.Equal(PluginErrorCode.PermissionDenied, (await rig.CallAsync(method, args)).Error);
        Assert.Equal(0, rig.Prefs.Puts);
        Assert.Empty(rig.Changed);
    }

    [Fact]
    public async Task Search_needs_no_permission_ranks_labels_above_pages_and_caps_the_answer()
    {
        var rig = await Rig.StartAsync();
        var hits = (await rig.CallAsync("app.searchSettings", """["time zone"]""")).Value!;
        Assert.True(hits.Count is > 0 and <= 8);
        var first = hits.Item(0);
        Assert.Contains("label", PluginApiRig.Keys(first));
        Assert.Contains("page", PluginApiRig.Keys(first));
        Assert.Contains("pageId", PluginApiRig.Keys(first));
        Assert.Contains(Enumerable.Range(0, hits.Count), i => hits.Item(i).Property("label").StringValue == "Time zone" && hits.Item(i).Property("pageId").StringValue == "general");
        Assert.Equal(2, (await rig.CallAsync("app.searchSettings", """["notifications sound",{"limit":2}]""")).Value!.Count);
        Assert.True((await rig.CallAsync("app.searchSettings", """["a",{"limit":1000}]""")).Value!.Count == 0, "single letters are not words");
        Assert.True((await rig.CallAsync("app.searchSettings", """["settings",{"limit":1000}]""")).Value!.Count <= PluginAppSearchSettings.MaxResults);
        Assert.Equal(0, (await rig.CallAsync("app.searchSettings", """["zzqqxx"]""")).Value!.Count);
        foreach (var bad in new[] { "[]", "[5]", """["   "]""", """["x y",5]""", """["x y",[]]""" })
        {
            Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("app.searchSettings", bad)).Error);
        }
    }

    [Fact]
    public async Task GetSettings_reports_plain_values_with_defaults_and_nothing_that_is_a_secret()
    {
        var rig = await Rig.StartAsync("settings:read");
        var all = (await rig.CallAsync("app.getSettings", "[]")).Value!;
        var keys = PluginApiRig.Keys(all);
        Assert.Equal("medium", all.Property("fontSize").StringValue, StringComparer.Ordinal);
        Assert.Equal("50", all.Property("emailsPerPage").NumberText, StringComparer.Ordinal);
        Assert.True(all.Property("showPreview").BooleanValue);
        Assert.Equal("system", all.Property("theme").StringValue, StringComparer.Ordinal);
        Assert.Equal("month", all.Property("calendarView").StringValue, StringComparer.Ordinal);
        Assert.True(all.Property("animationsEnabled").BooleanValue);
        foreach (var secret in new[] { "pushRelayUrl", "trustedSenders", "sessionTimeout", "debugMode", "signatureDetails", "externalContentPolicy", "emailKeywords", "password", "token" })
        {
            Assert.DoesNotContain(secret, keys);
        }

        foreach (var key in keys)
        {
            Assert.True(all.Property(key).Kind is PluginValueKind.String or PluginValueKind.Boolean or PluginValueKind.Number, key);
        }

        rig.Prefs.Put("reduceMotion", JsonValue.Create("on"));
        rig.Prefs.Put("emailsPerPage", JsonValue.Create(25));
        var changed = (await rig.CallAsync("app.getSettings", "[]")).Value!;
        Assert.False(changed.Property("animationsEnabled").BooleanValue);
        Assert.Equal("on", changed.Property("reduceMotion").StringValue, StringComparer.Ordinal);
        Assert.Equal("25", changed.Property("emailsPerPage").NumberText, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Describe_names_the_setting_its_page_the_values_and_whether_a_change_may_be_made()
    {
        var rig = await Rig.StartAsync("settings:read");
        var out1 = (await rig.CallAsync("app.describeSettings", """[{"density":"compact","emailsPerPage":"25","pushRelayUrl":"x","nope":1,"confirmDelete":false,"toolbarPosition":"top"}]""")).Value!;
        var density = out1.Property("density");
        Assert.Equal(["allowed", "current", "currentLabel", "key", "label", "next", "nextLabel", "options", "page", "pageLabel"], PluginApiRig.Keys(density), StringComparer.Ordinal);
        Assert.True(density.Property("allowed").BooleanValue);
        Assert.Equal("regular", density.Property("current").StringValue, StringComparer.Ordinal);
        Assert.Equal("appearance", density.Property("page").StringValue, StringComparer.Ordinal);
        Assert.Equal("Appearance", density.Property("pageLabel").StringValue, StringComparer.Ordinal);
        Assert.Equal("compact", density.Property("next").StringValue, StringComparer.Ordinal);
        Assert.Equal(4, density.Property("options").Count);
        // a string where the row holds numbers, a name that is refused, a name nobody knows, a safety net turned off, a key nothing reads
        Assert.Equal("bad_value", out1.Property("emailsPerPage").Property("reason").StringValue, StringComparer.Ordinal);
        Assert.False(out1.Property("emailsPerPage").Property("allowed").BooleanValue);
        Assert.Equal("not_allowed", out1.Property("pushRelayUrl").Property("reason").StringValue, StringComparer.Ordinal);
        Assert.Equal("", out1.Property("pushRelayUrl").Property("page").StringValue, StringComparer.Ordinal);
        Assert.Equal("unknown", out1.Property("nope").Property("reason").StringValue, StringComparer.Ordinal);
        Assert.Equal("bad_value", out1.Property("confirmDelete").Property("reason").StringValue, StringComparer.Ordinal);
        Assert.Equal("unavailable", out1.Property("toolbarPosition").Property("reason").StringValue, StringComparer.Ordinal);
        // the list form, a switch and its labels (disableThreading says the opposite of what it stores)
        var named = (await rig.CallAsync("app.describeSettings", """[["disableThreading","timeFormat","theme"]]""")).Value!;
        Assert.Equal("On", named.Property("disableThreading").Property("currentLabel").StringValue, StringComparer.Ordinal);
        Assert.Equal(["false", "true"], Enumerable.Range(0, 2).Select(i => named.Property("disableThreading").Property("options").Item(i).Property("value").BooleanValue ? "true" : "false").Order(StringComparer.Ordinal), StringComparer.Ordinal);
        Assert.Equal("24h", named.Property("timeFormat").Property("current").StringValue, StringComparer.Ordinal);
        Assert.Equal("System", named.Property("theme").Property("currentLabel").StringValue, StringComparer.Ordinal);
        // limits: none, more than 20, a name that is not text, a name over 100 characters is skipped
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("app.describeSettings", "[[]]")).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("app.describeSettings", "[{}]")).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("app.describeSettings", "[[" + string.Join(",", Enumerable.Range(0, 21).Select(i => $"\"k{i}\"")) + "]]")).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("app.describeSettings", "[[5]]")).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("app.describeSettings", "[]")).Error);
        var long1 = (await rig.CallAsync("app.describeSettings", $$"""[["{{new string('k', 101)}}","density"]]""")).Value!;
        Assert.Equal(["density"], PluginApiRig.Keys(long1), StringComparer.Ordinal);
        Assert.Equal(0, rig.Prefs.Puts);
    }

    [Fact]
    public async Task Set_writes_through_the_store_reports_old_and_new_and_refuses_what_is_not_allowed_key_by_key()
    {
        var rig = await Rig.StartAsync("settings:change");
        var result = await rig.CallAsync("app.setSettings", """[{"density":"compact","emailsPerPage":25,"showPreview":true,"timeFormat":"12h","timeZone":"Europe/Berlin","theme":"dark","calendarView":"week","messageListOrderScope":"all","confirmDelete":true,"locale":"de"}]""");
        Assert.True(result.IsSuccess);
        var value = result.Value!;
        Assert.Equal(["applied", "refused", "undoToken"], PluginApiRig.Keys(value), StringComparer.Ordinal);
        var applied = value.Property("applied");
        Assert.Equal(["calendarView", "confirmDelete", "density", "emailsPerPage", "messageListOrderScope", "showPreview", "theme", "timeFormat", "timeZone"], PluginApiRig.Keys(applied), StringComparer.Ordinal);
        Assert.Equal(["old", "new"], applied.Property("density").PropertyNames, StringComparer.Ordinal);
        Assert.Equal("regular", applied.Property("density").Property("old").StringValue, StringComparer.Ordinal);
        Assert.Equal("compact", applied.Property("density").Property("new").StringValue, StringComparer.Ordinal);
        Assert.Equal("unknown", value.Property("refused").Property("locale").StringValue, StringComparer.Ordinal);
        // each went through the store the pages use: the file value, the number kept a number, the keyless rows under their own keys
        Assert.Equal("compact", rig.Stored("density"), StringComparer.Ordinal);
        Assert.Equal(25d, rig.Prefs.Current.Values["emailsPerPage"]!.GetValue<double>());
        Assert.Equal("dark", rig.Stored("theme"), StringComparer.Ordinal);
        Assert.Equal("week", rig.Stored("calendarViewMode"), StringComparer.Ordinal);
        Assert.Equal("Europe/Berlin", rig.Stored("timeZone"), StringComparer.Ordinal);
        Assert.Equal("all", rig.Stored("messageListOrderScope"), StringComparer.Ordinal);
        Assert.True((bool)rig.Prefs.Current.Values["confirmDelete"]!);
        Assert.Contains("density", rig.Changed);
        // a value equal to the current one is applied and writes nothing
        var puts = rig.Prefs.Puts;
        var same = (await rig.CallAsync("app.setSettings", """[{"density":"compact"}]""")).Value!;
        Assert.Equal("compact", same.Property("applied").Property("density").Property("new").StringValue, StringComparer.Ordinal);
        Assert.Equal(PluginValueKind.Null, same.Property("undoToken").Kind);
        Assert.Equal(puts, rig.Prefs.Puts);
    }

    [Fact]
    public async Task Set_refuses_every_name_value_and_direction_the_table_does_not_allow_and_writes_none_of_them()
    {
        var rig = await Rig.StartAsync("settings:change");
        (string Json, string Reason)[] refusals =
        [
            ("""{"pushRelayUrl":"https://x"}""", "not_allowed"), ("""{"externalContentPolicy":"always"}""", "not_allowed"), ("""{"sessionTimeout":0}""", "not_allowed"), ("""{"signatureDetails":"x"}""", "not_allowed"),
            ("""{"debugMode":true}""", "not_allowed"), ("""{"trustedSenders":"x"}""", "not_allowed"), ("""{"sidebarApps":"x"}""", "not_allowed"), ("""{"proofreading":true}""", "not_allowed"),
            ("""{"showAdvancedSettings":true}""", "unknown"), ("""{"isAdmin":true}""", "unknown"), ("""{"__proto__":1}""", "unknown"), ("""{"constructor":1}""", "unknown"), ("""{"locale":"de"}""", "unknown"),
            ("""{"toolbarPosition":"top"}""", "unavailable"), ("""{"swipeLeftAction":"delete"}""", "unavailable"),
            ("""{"interfaceLayout":"zzz"}""", "bad_value"), ("""{"fontSize":5}""", "bad_value"), ("""{"emailsPerPage":"25"}""", "bad_value"), ("""{"emailsPerPage":26}""", "bad_value"), ("""{"density":null}""", "bad_value"),
            ("""{"density":["compact"]}""", "bad_value"), ("""{"density":{"a":1}}""", "bad_value"), ("""{"showPreview":"true"}""", "bad_value"), ("""{"showPreview":1}""", "bad_value"), ("""{"timeZone":"Not/AZone"}""", "bad_value"),
            ("""{"timeZone":"../etc"}""", "bad_value"), ("""{"timeZone":""}""", "bad_value"), ("""{"theme":"purple"}""", "bad_value"), ("""{"deleteAction":"permanent"}""", "bad_value"), ("""{"sendDelaySeconds":60}""", "bad_value"),
            ("""{"confirmDelete":false}""", "bad_value"), ("""{"confirmBulkDelete":false}""", "bad_value"), ("""{"permanentlyDeleteJunk":true}""", "bad_value"), ("""{"senderFavicons":true}""", "bad_value"),
            ("""{"showAvatarsInJunk":true}""", "bad_value"), ("""{"messageListOrderScope":"everywhere"}""", "bad_value"),
        ];
        var made = 0;
        foreach (var (json, reason) in refusals)
        {
            // 30 changes a minute: a fresh minute every twenty
            if (++made % 20 == 0) rig.Time.Advance(TimeSpan.FromSeconds(61));
            var outcome = await rig.CallAsync("app.setSettings", "[" + json + "]");
            Assert.True(outcome.IsSuccess, json + " " + outcome.Error);
            var value = outcome.Value!;
            var name = JsonNode.Parse(json)!.AsObject().First().Key;
            Assert.Equal(reason, value.Property("refused").Property(name).StringValue, StringComparer.Ordinal);
            Assert.Empty(value.Property("applied").PropertyNames);
            Assert.Equal(PluginValueKind.Null, value.Property("undoToken").Kind);
        }

        Assert.Equal(0, rig.Prefs.Puts);
        // the safety nets move toward safe, and the others take their edge values
        var ok = (await rig.CallAsync("app.setSettings", """[{"confirmDelete":true,"confirmBulkDelete":true,"permanentlyDeleteJunk":false,"senderFavicons":false,"sendDelaySeconds":30,"mailTextScale":0.875,"toastDuration":0,"timeZone":"auto","firstDayOfWeek":6}]""")).Value!;
        Assert.Equal(9, ok.Property("applied").Count + ok.Property("refused").Count);
        Assert.Equal(0, ok.Property("refused").Count);
    }

    [Fact]
    public async Task Set_checks_the_size_of_the_call_and_the_arguments()
    {
        var rig = await Rig.StartAsync("settings:change");
        foreach (var bad in new[] { "[]", "[{}]", "[[]]", "[5]", "[null]", "[\"density\"]", "[{\"" + new string('k', 101) + "\":1}]", "[" + "{" + string.Join(",", Enumerable.Range(0, 21).Select(i => $"\"k{i}\":1")) + "}]" })
        {
            Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("app.setSettings", bad)).Error);
        }

        var twenty = "[{" + string.Join(",", Enumerable.Range(0, 20).Select(i => $"\"k{i}\":1")) + "}]";
        Assert.Equal(20, (await rig.CallAsync("app.setSettings", twenty)).Value!.Property("refused").Count);
        Assert.Equal(0, rig.Prefs.Puts);
    }

    [Fact]
    public async Task An_add_on_that_stops_the_change_makes_it_stopped_and_gets_no_token()
    {
        var rig = await Rig.StartAsync("settings:change");
        using var veto = rig.Hooks.Intercept<SettingChangePayload>(HookNames.OnBeforeSettingChange, (payload, _) => ValueTask.FromResult(!string.Equals(payload.Key, "density", StringComparison.Ordinal)));
        var value = (await rig.CallAsync("app.setSettings", """[{"density":"compact","showPreview":false}]""")).Value!;
        Assert.Equal("stopped", value.Property("refused").Property("density").StringValue, StringComparer.Ordinal);
        Assert.Equal(["showPreview"], PluginApiRig.Keys(value.Property("applied")), StringComparer.Ordinal);
        Assert.Null(rig.Prefs.Current.Values["density"]);
        Assert.False((bool)rig.Prefs.Current.Values["showPreview"]!);
        Assert.NotEqual(PluginValueKind.Null, value.Property("undoToken").Kind);
    }

    [Fact]
    public async Task Undo_puts_back_exactly_what_was_changed_once_and_only_for_the_add_on_that_changed_it()
    {
        var rig = await Rig.StartAsync("settings:change");
        var other = rig.Api.BindOther("other", ["settings:change"]);
        var set = (await rig.CallAsync("app.setSettings", """[{"density":"compact","emailsPerPage":100,"theme":"dark","showPreview":true}]""")).Value!;
        var token = set.Property("undoToken").StringValue;
        Assert.Equal(32, token.Length);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.Api.CallAsync(other, "app.undoSettings", $"[\"{token}\"]")).Error);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("app.undoSettings", """["not-a-token"]""")).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("app.undoSettings", "[5]")).Error);
        var undone = (await rig.CallAsync("app.undoSettings", $"[\"{token}\"]")).Value!;
        Assert.Equal(["refused", "restored"], PluginApiRig.Keys(undone), StringComparer.Ordinal);
        Assert.Equal(["density", "emailsPerPage", "theme"], Enumerable.Range(0, undone.Property("restored").Count).Select(i => undone.Property("restored").Item(i).StringValue).Order(StringComparer.Ordinal), StringComparer.Ordinal);
        Assert.Equal(0, undone.Property("refused").Count);
        Assert.Equal("regular", rig.Stored("density"), StringComparer.Ordinal);
        Assert.Equal(50d, rig.Prefs.Current.Values["emailsPerPage"]!.GetValue<double>());
        Assert.Equal("system", rig.Stored("theme"), StringComparer.Ordinal);
        // spent, and a token also runs out after 30 minutes
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("app.undoSettings", $"[\"{token}\"]")).Error);
        var later = (await rig.CallAsync("app.setSettings", """[{"density":"compact"}]""")).Value!.Property("undoToken").StringValue;
        rig.Time.Advance(TimeSpan.FromMinutes(31));
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("app.undoSettings", $"[\"{later}\"]")).Error);
        Assert.Equal("compact", rig.Stored("density"), StringComparer.Ordinal);
    }

    [Fact]
    public async Task Undo_does_not_write_what_another_add_on_stops_and_keeps_going()
    {
        var rig = await Rig.StartAsync("settings:change");
        var token = (await rig.CallAsync("app.setSettings", """[{"density":"compact","showPreview":false}]""")).Value!.Property("undoToken").StringValue;
        using var veto = rig.Hooks.Intercept<SettingChangePayload>(HookNames.OnBeforeSettingChange, (payload, _) => ValueTask.FromResult(!string.Equals(payload.Key, "density", StringComparison.Ordinal)));
        var undone = (await rig.CallAsync("app.undoSettings", $"[\"{token}\"]")).Value!;
        Assert.Equal("stopped", undone.Property("refused").Property("density").StringValue, StringComparer.Ordinal);
        Assert.Equal("showPreview", undone.Property("restored").Item(0).StringValue, StringComparer.Ordinal);
        Assert.Equal("compact", rig.Stored("density"), StringComparer.Ordinal);
    }

    [Fact]
    public async Task A_stale_session_before_a_write_writes_nothing_and_one_that_goes_stale_during_it_is_an_unknown_outcome()
    {
        var rig = await Rig.StartAsync("settings:change", "settings:read");
        rig.Api.Active.Valid = false;
        Assert.Equal(PluginErrorCode.NoSession, (await rig.CallAsync("app.setSettings", """[{"density":"compact"}]""")).Error);
        Assert.Equal(PluginErrorCode.NoSession, (await rig.CallAsync("app.getSettings", "[]")).Error);
        Assert.Equal(0, rig.Prefs.Puts);
        rig.Api.Active.Valid = true;
        using var flip = rig.Hooks.Intercept<SettingChangePayload>(HookNames.OnBeforeSettingChange, (_, _) =>
        {
            rig.Api.Active.Valid = false;
            return ValueTask.FromResult(true);
        });
        Assert.Equal(PluginErrorCode.UnknownOutcome, (await rig.CallAsync("app.setSettings", """[{"density":"compact"}]""")).Error);
        // a read that goes stale gives no data
        rig.Api.Active.Valid = true;
        rig.Prefs.OnRead = () => rig.Api.Active.Valid = false;
        Assert.Equal(PluginErrorCode.StaleContext, (await rig.CallAsync("app.getSettings", "[]")).Error);
        rig.Api.Active.Valid = true;
        Assert.Equal(PluginErrorCode.StaleContext, (await rig.CallAsync("app.describeSettings", """[["density"]]""")).Error);
    }

    [Fact]
    public async Task A_revoked_add_on_writes_nothing()
    {
        var rig = await Rig.StartAsync("settings:change");
        using var revoke = rig.Hooks.Intercept<SettingChangePayload>(HookNames.OnBeforeSettingChange, (_, _) =>
        {
            rig.Api.Auth.Current = PluginApiRig.Grant(["settings:change"], enabled: false);
            return ValueTask.FromResult(true);
        });
        // the first key passes its check, writes, and the next one finds the grant gone before it writes
        var outcome = await rig.CallAsync("app.setSettings", """[{"density":"compact","showPreview":false}]""");
        Assert.Equal(PluginErrorCode.UnknownOutcome, outcome.Error);
        Assert.Null(rig.Prefs.Current.Values["showPreview"]);
    }

    [Fact]
    public async Task Writes_are_limited_to_thirty_a_minute_and_reads_to_sixty()
    {
        var rig = await Rig.StartAsync("settings:change", "settings:read");
        for (var i = 0; i < 30; i++) Assert.True((await rig.CallAsync("app.setSettings", """[{"density":"compact"}]""")).IsSuccess);
        Assert.Equal(PluginErrorCode.RateLimited, (await rig.CallAsync("app.setSettings", """[{"density":"regular"}]""")).Error);
        Assert.Equal(PluginErrorCode.RateLimited, (await rig.CallAsync("app.undoSettings", """["x"]""")).Error);
        for (var i = 0; i < 60; i++) Assert.True((await rig.CallAsync("app.describeSettings", """[["density"]]""")).IsSuccess);
        Assert.Equal(PluginErrorCode.RateLimited, (await rig.CallAsync("app.getSettings", "[]")).Error);
        Assert.Equal(PluginErrorCode.RateLimited, (await rig.CallAsync("app.searchSettings", """["time"]""")).Error);
        rig.Time.Advance(TimeSpan.FromSeconds(61));
        Assert.True((await rig.CallAsync("app.setSettings", """[{"density":"regular"}]""")).IsSuccess);
        Assert.True((await rig.CallAsync("app.getSettings", "[]")).IsSuccess);
    }

    [Fact]
    public void The_table_matches_the_catalog_never_holds_a_denied_key_and_every_value_it_offers_passes_the_store_rules()
    {
        var denied = PluginSettingsTable.DeniedNames;
        Assert.NotEmpty(PluginSettingsTable.AllowedNames);
        foreach (var name in PluginSettingsTable.AllowedNames)
        {
            Assert.DoesNotContain(name, denied);
            var (setting, refusal) = PluginSettingsTable.Resolve(name);
            Assert.NotNull(setting);
            Assert.True(refusal is null or PluginSettingRefusal.Unavailable, name);
            var rule = setting.Rule;
            switch (rule.Kind)
            {
                case PluginSettingKind.Boolean:
                    Assert.Equal(SettingRowKind.Toggle, setting.Row.Kind);
                    break;
                case PluginSettingKind.Choice:
                    Assert.NotEmpty(PluginSettingValues.Choices(setting));
                    foreach (var (value, _) in PluginSettingValues.Choices(setting))
                    {
                        Assert.True(setting.Row.Key is null || SettingRules.Coerce(setting.Row, value).Ok, name + "=" + value.ToJsonString());
                        Assert.True(PluginSettingValues.Allows(setting, value, out _), name);
                    }

                    break;
                default:
                    break;
            }
        }

        // nothing a person could call a credential, server setting or security toggle is reachable by name
        foreach (var name in new[] { "password", "token", "pushRelayUrl", "sessionTimeout", "debugMode", "adminKey", "externalContentPolicy", "trustedSenders", "signatureDetails", "proofreading", "sidebarApps", "keepAppsLoaded", "settingsSyncDisabled", "emailKeywords" })
        {
            Assert.DoesNotContain(name, PluginSettingsTable.AllowedNames);
            Assert.Null(PluginSettingsTable.Resolve(name).Setting);
        }

        Assert.Equal(PluginSettingsTable.Shared.Count, PluginSettingsTable.Shared.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain("pushRelayUrl", PluginSettingsTable.Shared);
        Assert.DoesNotContain("sessionTimeout", PluginSettingsTable.Shared);
        foreach (var key in PluginSettingsTable.Shared)
        {
            Assert.DoesNotContain(key, denied.Where(d => d is "pushRelayUrl" or "signatureDetails" or "trustedSenders" or "emailKeywords" or "debugMode"));
        }
    }
}
