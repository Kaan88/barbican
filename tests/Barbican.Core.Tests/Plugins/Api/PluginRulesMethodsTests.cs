// Source: Bulwark lib/plugin-sandbox/assistant/c.filters.ts (filters.list, filters.create, filters.undo: the rule limits, the refused actions, folders of the filters' own account that nobody shares, the undo token), host-api.ts:302-305,1957-1982,
// 2403-2415 (sieve.isSupported, getActiveScript, validateScript, regenerate) and assistant/c.ts:27-29 (filters:read / filters:write). The real PluginApiDispatcher runs over the real FilterService on FakeJmap, so every write goes through
// the service Settings > Filters uses (its hooks, state check and script generation); Barbican fences: denied calls reach no server, a stale account before a write writes nothing and one during it reports an unknown outcome.
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Sieve;
using Barbican.FakeJmap;

namespace Barbican.Core.Tests.Plugins.Api;

public sealed class PluginRulesMethodsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Create(string mailboxId) =>
        $$"""[{"name":"News","match":"any","conditions":[{"field":"from","op":"contains","value":["news@example.org","digest@example.org"]},{"field":"size","op":"greater_than","value":1000}],"actions":[{"type":"move","mailboxId":"{{mailboxId}}"},{"type":"mark_read"},{"type":"add_label","tagId":"red"},{"type":"stop"}]}]""";

    private static Task<PluginRulesRig> StartAsync(params string[] permissions) => PluginRulesRig.StartAsync(permissions);

    [Fact]
    public async Task Create_adds_the_rule_at_the_top_through_the_filter_service_and_list_shows_it()
    {
        var rig = await StartAsync("filters:read", "filters:write");
        var archive = rig.Folder("archive");
        var saved = 0;
        using var observe = rig.Hooks.Observe<FilterAccountPayload>(HookNames.OnFiltersSave, (_, _) => { Interlocked.Increment(ref saved); return ValueTask.CompletedTask; });
        var created = await rig.CallAsync("filters.create", Create(archive));
        Assert.True(created.IsSuccess);
        Assert.Equal(["id", "undoToken"], PluginApiRig.Keys(created.Value!), StringComparer.Ordinal);
        var script = rig.Api.ActiveSieveText()!;
        Assert.Contains("News", script, StringComparison.Ordinal);
        var state = (await rig.Filters.LoadAsync("a1", Ct)).Value!;
        var rule = Assert.Single(state.Rules);
        Assert.Equal(created.Value!.Property("id").StringValue, (string?)rule["id"], StringComparer.Ordinal);
        Assert.Equal("any", (string?)rule["matchType"], StringComparer.Ordinal);
        Assert.True((bool)rule["stopProcessing"]!);
        Assert.Equal(["move", "mark_read", "add_label"], rule["actions"]!.AsArray().Select(a => (string?)a!["type"]).ToArray(), StringComparer.Ordinal);
        Assert.Equal("1000", (string?)rule["conditions"]![1]!["value"], StringComparer.Ordinal);
        Assert.True(await WaitAsync(() => Volatile.Read(ref saved) == 1));

        var list = (await rig.CallAsync("filters.list", "[]")).Value!;
        Assert.Equal(["editable", "rules", "supported"], PluginApiRig.Keys(list), StringComparer.Ordinal);
        Assert.True(list.Property("supported").BooleanValue);
        Assert.True(list.Property("editable").BooleanValue);
        var info = list.Property("rules").Item(0);
        Assert.Equal(["actions", "conditions", "editable", "id", "isActive", "match", "name"], PluginApiRig.Keys(info), StringComparer.Ordinal);
        Assert.Equal("any", info.Property("match").StringValue, StringComparer.Ordinal);
        Assert.True(info.Property("isActive").BooleanValue);
        Assert.True(info.Property("editable").BooleanValue);
        Assert.Equal(["field", "op", "value"], PluginApiRig.Keys(info.Property("conditions").Item(0)), StringComparer.Ordinal);
        Assert.Equal(2, info.Property("conditions").Item(0).Property("value").Count);
        var move = info.Property("actions").Item(0);
        Assert.Equal(["folder", "mailboxId", "type"], PluginApiRig.Keys(move), StringComparer.Ordinal);
        Assert.Equal(archive, move.Property("mailboxId").StringValue, StringComparer.Ordinal);
        Assert.Equal("Archive", move.Property("folder").StringValue, StringComparer.Ordinal);
        Assert.Equal(["type"], PluginApiRig.Keys(info.Property("actions").Item(1)), StringComparer.Ordinal);
        Assert.Equal("red", info.Property("actions").Item(2).Property("tagId").StringValue, StringComparer.Ordinal);
    }

    [Fact]
    public async Task List_shows_a_rule_written_elsewhere_as_not_editable_and_names_only_the_known_fields()
    {
        var rig = await StartAsync("filters:read");
        var rule = SieveScript.Rule("Forwarder", "from", "contains", "boss@example.org", "forward", "someone@example.net", false);
        rule["origin"] = "other-client";
        rule["secretField"] = "never shown";
        var loaded = await rig.Filters.LoadAsync("a1", Ct);
        Assert.True((await rig.Filters.SaveAsync("a1", [rule], loaded.Value!.State, Ct)).IsOk);
        var info = (await rig.CallAsync("filters.list", "[]")).Value!.Property("rules").Item(0);
        Assert.False(info.Property("editable").BooleanValue);
        Assert.DoesNotContain(info.PropertyNames, n => string.Equals(n, "secretField", StringComparison.Ordinal));
        Assert.Equal("someone@example.net", info.Property("actions").Item(0).Property("value").StringValue, StringComparer.Ordinal);
    }

    [Fact]
    public async Task List_without_filters_on_the_server_says_so_and_a_hand_written_script_is_not_editable()
    {
        var plain = await PluginRulesRig.StartAsync(["filters:read"], new FakeJmapOptions { VacationCapability = true, MetadataCapability = true });
        var none = (await plain.CallAsync("filters.list", "[]")).Value!;
        Assert.False(none.Property("supported").BooleanValue);
        Assert.False(none.Property("editable").BooleanValue);
        Assert.Equal(0, none.Property("rules").Count);
        Assert.False((await plain.CallAsync("sieve.isSupported", "[]")).Value!.BooleanValue);
        Assert.Equal(PluginValueKind.Null, (await plain.CallAsync("sieve.getActiveScript", "[]")).Value!.Kind);

        var rig = await StartAsync("filters:read", "filters:write");
        rig.Api.SeedSieve("active", "/* @metadata:begin\n{\"version\": 3, \"rules\": []}\n@metadata:end */\n", true);
        var locked = (await rig.CallAsync("filters.list", "[]")).Value!;
        Assert.True(locked.Property("supported").BooleanValue);
        Assert.False(locked.Property("editable").BooleanValue);
        Assert.Equal(0, locked.Property("rules").Count);
        var before = rig.Api.ActiveSieveText();
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("filters.create", Create(rig.Folder("archive")))).Error);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("sieve.regenerate", "[]")).Error);
        Assert.Equal(before, rig.Api.ActiveSieveText());
    }

    [Theory]
    [InlineData("""{"type":"forward","value":"a@example.org"}""")]
    [InlineData("""{"type":"redirect","value":"a@example.org"}""")]
    [InlineData("""{"type":"reject","value":"no"}""")]
    [InlineData("""{"type":"discard"}""")]
    [InlineData("""{"type":"keep"}""")]
    [InlineData("""{"type":"star","mailboxId":"x"}""")]
    [InlineData("""{"type":"mark_read","tagId":"red"}""")]
    [InlineData("""{"type":"add_label","tagId":"no-such-tag"}""")]
    [InlineData("""{"type":"add_label"}""")]
    [InlineData("""{"type":"move"}""")]
    [InlineData("""{"type":"move","mailboxId":"no-such-folder"}""")]
    [InlineData("""{"type":"move","mailboxId":"@trash"}""")]
    [InlineData("""{"type":"copy","mailboxId":"@inbox","tagId":"red"}""")]
    [InlineData("""{"type":"move","mailboxId":"@inbox","extra":1}""")]
    [InlineData("""{"type":"stop"}""")]
    public async Task Create_refuses_an_action_that_sends_mail_elsewhere_drops_it_or_names_no_folder_of_the_user(string action)
    {
        var rig = await StartAsync("filters:write");
        action = action.Replace("@trash", rig.Folder("trash"), StringComparison.Ordinal).Replace("@inbox", rig.Folder("inbox"), StringComparison.Ordinal);
        var result = await rig.CallAsync("filters.create", $$"""[{"name":"R","conditions":[{"field":"subject","op":"contains","value":"x"}],"actions":[{{action}}]}]""");
        Assert.Equal(PluginErrorCode.InvalidArguments, result.Error);
        Assert.Null(rig.Api.ActiveSieveText());
    }

    [Theory]
    [InlineData("""{"name":"","conditions":[{"field":"subject","op":"contains","value":"x"}],"actions":[{"type":"mark_read"}]}""")]
    [InlineData("""{"name":"R","match":"some","conditions":[{"field":"subject","op":"contains","value":"x"}],"actions":[{"type":"mark_read"}]}""")]
    [InlineData("""{"name":"R","conditions":[],"actions":[{"type":"mark_read"}]}""")]
    [InlineData("""{"name":"R","conditions":[{"field":"subject","op":"contains","value":"x"}],"actions":[]}""")]
    [InlineData("""{"name":"R","extra":1,"conditions":[{"field":"subject","op":"contains","value":"x"}],"actions":[{"type":"mark_read"}]}""")]
    [InlineData("""{"name":"R","conditions":[{"field":"body","op":"is","value":"x"}],"actions":[{"type":"mark_read"}]}""")]
    [InlineData("""{"name":"R","conditions":[{"field":"nope","op":"is","value":"x"}],"actions":[{"type":"mark_read"}]}""")]
    [InlineData("""{"name":"R","conditions":[{"field":"header","op":"is","value":"x"}],"actions":[{"type":"mark_read"}]}""")]
    [InlineData("""{"name":"R","conditions":[{"field":"header","op":"is","value":"x","headerName":"bad name"}],"actions":[{"type":"mark_read"}]}""")]
    [InlineData("""{"name":"R","conditions":[{"field":"subject","op":"is","value":"x","headerName":"X-A"}],"actions":[{"type":"mark_read"}]}""")]
    [InlineData("""{"name":"R","conditions":[{"field":"size","op":"greater_than","value":"12x"}],"actions":[{"type":"mark_read"}]}""")]
    [InlineData("""{"name":"R","conditions":[{"field":"size","op":"greater_than","value":-5}],"actions":[{"type":"mark_read"}]}""")]
    [InlineData("""{"name":"R","conditions":[{"field":"attachment","op":"has_any","value":"x"}],"actions":[{"type":"mark_read"}]}""")]
    [InlineData("""{"name":"R","conditions":[{"field":"subject","op":"is","value":"line\nbreak"}],"actions":[{"type":"mark_read"}]}""")]
    [InlineData("""{"name":"R","conditions":[{"field":"subject","op":"is","value":[]}],"actions":[{"type":"mark_read"}]}""")]
    [InlineData("""{"name":"R","conditions":[{"field":"subject","op":"is"}],"actions":[{"type":"mark_read"}]}""")]
    public async Task Create_refuses_a_rule_the_rule_editor_would_refuse_and_writes_nothing(string rule)
    {
        var rig = await StartAsync("filters:write");
        var calls = rig.F.Server.ApiCalls.Count;
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("filters.create", $"[{rule}]")).Error);
        Assert.Equal(calls, rig.F.Server.ApiCalls.Count);
        Assert.Null(rig.Api.ActiveSieveText());
    }

    [Fact]
    public async Task Create_holds_the_documented_limits_at_their_edges()
    {
        var rig = await StartAsync("filters:write");
        string Conditions(int n) => string.Join(',', Enumerable.Range(0, n).Select(_ => """{"field":"subject","op":"contains","value":"x"}"""));
        string Values(int n) => string.Join(',', Enumerable.Range(0, n).Select(i => $"\"v{i}\""));
        string Rule(string name, string conditions) => $$"""[{"name":"{{name}}","conditions":[{{conditions}}],"actions":[{"type":"star"}]}]""";
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("filters.create", Rule("R", Conditions(11)))).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("filters.create", Rule(new string('n', 101), Conditions(1)))).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("filters.create", Rule("R", $$"""{"field":"subject","op":"contains","value":[{{Values(21)}}]}"""))).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("filters.create", Rule("R", $$"""{"field":"subject","op":"contains","value":"{{new string('v', 501)}}"}"""))).Error);
        Assert.Null(rig.Api.ActiveSieveText());
        Assert.True((await rig.CallAsync("filters.create", Rule(new string('n', 100), Conditions(10)))).IsSuccess);
        Assert.True((await rig.CallAsync("filters.create", Rule("R2", $$"""{"field":"subject","op":"contains","value":[{{Values(20)}}]}"""))).IsSuccess);
        Assert.True((await rig.CallAsync("filters.create", Rule("R3", $$"""{"field":"subject","op":"contains","value":"{{new string('v', 500)}}"}"""))).IsSuccess);
        Assert.Equal(3, (await rig.Filters.LoadAsync("a1", Ct)).Value!.Rules.Count);
    }

    [Theory]
    [InlineData("filters.list", "[]", "filters:write")]
    [InlineData("filters.create", """[{"name":"R","conditions":[{"field":"subject","op":"contains","value":"x"}],"actions":[{"type":"star"}]}]""", "filters:read")]
    [InlineData("filters.undo", """["abc"]""", "filters:read")]
    [InlineData("sieve.isSupported", "[]", "filters:write")]
    [InlineData("sieve.getActiveScript", "[]", "filters:write")]
    [InlineData("sieve.validateScript", """["keep;"]""", "filters:read")]
    [InlineData("sieve.regenerate", "[]", "filters:read")]
    public async Task Each_method_is_denied_without_its_own_permission_and_reaches_no_server(string method, string args, string held)
    {
        var rig = await StartAsync(held);
        var calls = rig.F.Server.ApiCalls.Count;
        Assert.Equal(PluginErrorCode.PermissionDenied, (await rig.CallAsync(method, args)).Error);
        Assert.Equal(calls, rig.F.Server.ApiCalls.Count);
        Assert.Null(rig.Api.ActiveSieveText());
    }

    [Fact]
    public async Task Undo_takes_the_rule_out_once_and_only_for_the_add_on_and_login_that_made_it()
    {
        var rig = await StartAsync("filters:write", "filters:read");
        var other = rig.BindOther("other-addon", ["filters:write"]);
        var created = (await rig.CallAsync("filters.create", Create(rig.Folder("archive")))).Value!;
        var token = created.Property("undoToken").StringValue;
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync(other, "filters.undo", $"[\"{token}\"]")).Error);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("filters.undo", """["not-a-token"]""")).Error);
        Assert.Single((await rig.Filters.LoadAsync("a1", Ct)).Value!.Rules);
        var undone = await rig.CallAsync("filters.undo", $"[\"{token}\"]");
        Assert.True(undone.IsSuccess);
        Assert.Equal(Barbican.Core.Plugins.Protocol.PluginValueKind.Undefined, undone.Value!.Kind);
        Assert.Empty((await rig.Filters.LoadAsync("a1", Ct)).Value!.Rules);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("filters.undo", $"[\"{token}\"]")).Error);
    }

    [Fact]
    public async Task Undo_is_a_conflict_when_the_filters_changed_since_and_keeps_the_token()
    {
        var rig = await StartAsync("filters:write");
        var first = (await rig.CallAsync("filters.create", Create(rig.Folder("archive")))).Value!;
        var token = first.Property("undoToken").StringValue;
        // the user edits their filters in Settings meanwhile
        var loaded = (await rig.Filters.LoadAsync("a1", Ct)).Value!;
        var own = SieveScript.Rule("Mine", "from", "contains", "me@example.org", "star", "", false);
        Assert.True((await rig.Filters.SaveAsync("a1", [own, .. loaded.Rules], loaded.State, Ct)).IsOk);
        var script = rig.Api.ActiveSieveText();
        Assert.Equal(PluginErrorCode.Conflict, (await rig.CallAsync("filters.undo", $"[\"{token}\"]")).Error);
        Assert.Equal(script, rig.Api.ActiveSieveText());
        Assert.Equal(2, (await rig.Filters.LoadAsync("a1", Ct)).Value!.Rules.Count);
    }

    [Fact]
    public async Task Create_is_a_conflict_when_the_script_changes_between_the_read_and_the_write_and_writes_nothing()
    {
        var rig = await StartAsync("filters:write");
        // another client puts a script in place as the write goes out: the state the rule was read at is gone
        rig.OnWrite = () => rig.Api.SeedSieve("active", "# another client", true);
        Assert.Equal(PluginErrorCode.Conflict, (await rig.CallAsync("filters.create", Create(rig.Folder("archive")))).Error);
        Assert.Equal("# another client", rig.Api.ActiveSieveText());
    }

    [Fact]
    public async Task Another_add_on_can_stop_the_save_and_nothing_is_written()
    {
        var rig = await StartAsync("filters:write");
        using var stop = rig.Hooks.Intercept<FilterRulesPayload>(HookNames.OnBeforeFiltersSave, (_, _) => ValueTask.FromResult(false));
        Assert.Equal(PluginErrorCode.Failed, (await rig.CallAsync("filters.create", Create(rig.Folder("archive")))).Error);
        Assert.Null(rig.Api.ActiveSieveText());
    }

    [Fact]
    public async Task A_stale_session_or_revoked_add_on_before_the_write_writes_nothing_and_one_during_it_is_an_unknown_outcome()
    {
        var rig = await StartAsync("filters:write");
        rig.OnRead = () => rig.Active.Valid = false;
        Assert.Equal(PluginErrorCode.StaleContext, (await rig.CallAsync("filters.create", Create(rig.Folder("archive")))).Error);
        Assert.Null(rig.Api.ActiveSieveText());
        rig.Active.Valid = true;
        rig.OnRead = () => rig.Auth.Current = PluginApiRig.Grant(["filters:write"], enabled: false);
        Assert.False((await rig.CallAsync("filters.create", Create(rig.Folder("archive")))).IsSuccess);
        Assert.Null(rig.Api.ActiveSieveText());

        var during = await StartAsync("filters:write");
        during.OnWrite = () => during.Active.Valid = false;
        var result = await during.CallAsync("filters.create", Create(during.Folder("archive")));
        Assert.Equal(PluginErrorCode.UnknownOutcome, result.Error);
        Assert.Null(result.Value);
        Assert.NotNull(during.Api.ActiveSieveText());
    }

    [Fact]
    public async Task Filter_writes_are_limited_to_thirty_a_minute_for_each_add_on()
    {
        var rig = await StartAsync("filters:write");
        var rule = """[{"name":"R","conditions":[{"field":"subject","op":"contains","value":"x"}],"actions":[{"type":"star"}]}]""";
        for (var i = 0; i < 30; i++) Assert.True((await rig.CallAsync("filters.create", rule)).IsSuccess, "call " + i);
        Assert.Equal(PluginErrorCode.RateLimited, (await rig.CallAsync("filters.create", rule)).Error);
        rig.Time.Advance(TimeSpan.FromSeconds(61));
        Assert.True((await rig.CallAsync("filters.create", rule)).IsSuccess);
    }

    [Fact]
    public async Task Sieve_reads_the_active_script_validates_text_and_regenerates_through_the_filter_service()
    {
        var rig = await StartAsync("filters:read", "filters:write");
        Assert.True((await rig.CallAsync("sieve.isSupported", "[]")).Value!.BooleanValue);
        Assert.Equal(Barbican.Core.Plugins.Protocol.PluginValueKind.Null, (await rig.CallAsync("sieve.getActiveScript", "[]")).Value!.Kind);
        Assert.True((await rig.CallAsync("filters.create", Create(rig.Folder("archive")))).IsSuccess);
        var active = (await rig.CallAsync("sieve.getActiveScript", "[]")).Value!;
        Assert.Equal(["content", "id", "name"], PluginApiRig.Keys(active), StringComparer.Ordinal);
        Assert.Equal(rig.Api.ActiveSieveText(), active.Property("content").StringValue, StringComparer.Ordinal);

        var good = (await rig.CallAsync("sieve.validateScript", """["keep;"]""")).Value!;
        Assert.Equal(["isValid"], PluginApiRig.Keys(good), StringComparer.Ordinal);
        Assert.True(good.Property("isValid").BooleanValue);
        var bad = (await rig.CallAsync("sieve.validateScript", """["fake-invalid-sieve"]""")).Value!;
        Assert.Equal(["errors", "isValid"], PluginApiRig.Keys(bad), StringComparer.Ordinal);
        Assert.False(bad.Property("isValid").BooleanValue);
        Assert.Equal(1, bad.Property("errors").Count);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("sieve.validateScript", "[1]")).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("sieve.validateScript", "[]")).Error);
        Assert.Equal(rig.Api.ActiveSieveText(), active.Property("content").StringValue, StringComparer.Ordinal);

        var generated = 0;
        using var transform = rig.Hooks.Transform<FilterScriptPayload>(HookNames.OnSieveScriptGenerate, (p, _) => { generated++; return ValueTask.FromResult(p with { Script = p.Script + "\n# managed by an add-on\n" }); });
        Assert.True((await rig.CallAsync("sieve.regenerate", "[]")).IsSuccess);
        Assert.Equal(1, generated);
        Assert.Contains("# managed by an add-on", rig.Api.ActiveSieveText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Regenerate_is_limited_to_ten_a_minute()
    {
        var rig = await StartAsync("filters:write");
        for (var i = 0; i < 10; i++) Assert.True((await rig.CallAsync("sieve.regenerate", "[]")).IsSuccess, "call " + i);
        Assert.Equal(PluginErrorCode.RateLimited, (await rig.CallAsync("sieve.regenerate", "[]")).Error);
    }

    private static async Task<bool> WaitAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++) await Task.Delay(20, Ct);
        return condition();
    }

}
