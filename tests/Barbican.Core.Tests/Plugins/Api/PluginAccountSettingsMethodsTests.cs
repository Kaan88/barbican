// Source: Bulwark lib/plugin-sandbox/host-api-settings.ts:125-255 (identities.update, vacation.get, vacation.set), assistant/c.ts:32-68 (vacation:read / vacation:write, forwardActive, no add-on turns the reply on while it forwards mail),
// host-api-account.ts:226-270 (accountData.get / set: own entry, 4096 bytes, 30 reads and 20 writes a minute), host-api.ts:217,2018-2022 (plugin.getConfig) and host-api-notify.ts (notifications.show: limits, one line, five a minute,
// the action's server must be one the add-on talks to). The real PluginApiDispatcher runs over the real VacationService, AccountIdentities and Inbox metadata writer on FakeJmap, so each change goes through the service its Settings
// page uses; Barbican fences: denied calls reach no server, a stale account before a write writes nothing and one during it reports an unknown outcome.
using System.Text.Json.Nodes;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Payloads;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Settings.Vacation;
using Barbican.FakeJmap;

namespace Barbican.Core.Tests.Plugins.Api;

public sealed class PluginAccountSettingsMethodsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly FakeJmapOptions Identities = new()
    {
        SieveCapability = true,
        VacationCapability = true,
        MetadataCapability = true,
        Identities = [new FakeIdentity("i7", "Ada", "ada@example.org") { TextSignature = "old text", ReplyTo = "old@example.org" }],
    };

    private static Task<PluginRulesRig> StartAsync(params string[] permissions) => PluginRulesRig.StartAsync(permissions);

    private static async Task SeedForwardAsync(PluginRulesRig rig, bool enabled)
    {
        var loaded = await rig.Vacation.LoadSnapshotAsync("a1", Ct);
        var saved = await rig.Vacation.SaveAsync("a1", loaded.Value! with { Forward = new VacationForward(enabled, "away@example.net", false) }, Ct);
        Assert.True(saved.IsOk, saved.Error?.Message);
    }

    // vacation

    [Fact]
    public async Task Vacation_get_returns_exactly_the_reply_fields_and_whether_it_forwards_mail()
    {
        var rig = await PluginRulesRig.StartAsync(["vacation:read"], new FakeJmapOptions
        {
            SieveCapability = true, VacationCapability = true, MetadataCapability = true,
            Vacation = new FakeVacation { IsEnabled = false, FromDate = "2026-12-01T00:00:00Z", ToDate = "2026-12-05T23:59:59Z", Subject = "Away", TextBody = "Back soon", HtmlBody = "<p>Back soon</p>" },
        });
        var view = (await rig.CallAsync("vacation.get", "[]")).Value!;
        Assert.Equal(["forwardActive", "fromDate", "htmlBody", "isEnabled", "subject", "textBody", "toDate"], PluginApiRig.Keys(view), StringComparer.Ordinal);
        Assert.Equal("Away", view.Property("subject").StringValue, StringComparer.Ordinal);
        Assert.Equal("2026-12-01T00:00:00Z", view.Property("fromDate").StringValue, StringComparer.Ordinal);
        Assert.Equal("2026-12-05T23:59:59Z", view.Property("toDate").StringValue, StringComparer.Ordinal);
        Assert.False(view.Property("isEnabled").BooleanValue);
        Assert.False(view.Property("forwardActive").BooleanValue);
        await SeedForwardAsync(rig, enabled: true);
        Assert.True((await rig.CallAsync("vacation.get", "[]")).Value!.Property("forwardActive").BooleanValue);
        rig.ShapeSnapshot = s => s with { SieveState = null };
        Assert.Equal(PluginValueKind.Null, (await rig.CallAsync("vacation.get", "[]")).Value!.Property("forwardActive").Kind);
    }

    [Fact]
    public async Task Vacation_without_the_server_capability_is_unavailable()
    {
        var rig = await PluginRulesRig.StartAsync(["vacation:read", "vacation:write"], new FakeJmapOptions { SieveCapability = true, MetadataCapability = true });
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("vacation.get", "[]")).Error);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("vacation.set", """[{"isEnabled":false}]""")).Error);
    }

    [Fact]
    public async Task Vacation_set_saves_through_the_service_cleans_the_html_and_keeps_what_the_add_on_cannot_see()
    {
        var rig = await StartAsync("vacation:write", "vacation:read");
        await SeedForwardAsync(rig, enabled: false);
        var result = await rig.CallAsync("vacation.set", """[{"isEnabled":true,"subject":"Out","htmlBody":"<p>Away until Monday</p><script>alert(1)</script>","fromDate":"2026-12-01T00:00:00Z","toDate":"2026-12-05T23:59:59+00:00"}]""");
        Assert.True(result.IsSuccess);
        Assert.Equal(["fromDate", "htmlBody", "isEnabled", "subject", "textBody", "toDate"], PluginApiRig.Keys(result.Value!), StringComparer.Ordinal);
        var server = rig.Api.CurrentVacation;
        Assert.True(server.IsEnabled);
        Assert.Equal("Out", server.Subject, StringComparer.Ordinal);
        Assert.DoesNotContain("script", server.HtmlBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Away until Monday", server.TextBody, StringComparison.Ordinal);
        Assert.Equal("2026-12-05T23:59:59Z", server.ToDate, StringComparer.Ordinal);
        Assert.Equal("away@example.net", (await rig.Vacation.LoadSnapshotAsync("a1", Ct)).Value!.Forward!.To, StringComparer.Ordinal);
        Assert.True(result.Value!.Property("isEnabled").BooleanValue);
    }

    [Fact]
    public async Task Vacation_set_cannot_turn_the_reply_on_while_it_forwards_mail_or_while_that_cannot_be_read_but_may_turn_it_off()
    {
        var rig = await StartAsync("vacation:write");
        await SeedForwardAsync(rig, enabled: true);
        var requests = rig.Api.VacationSetRequests.Count;
        Assert.Equal(PluginErrorCode.PermissionDenied, (await rig.CallAsync("vacation.set", """[{"isEnabled":true,"textBody":"Away"}]""")).Error);
        Assert.Equal(requests, rig.Api.VacationSetRequests.Count);
        Assert.False(rig.Api.CurrentVacation.IsEnabled);
        rig.ShapeSnapshot = s => s with { SieveState = null };
        Assert.Equal(PluginErrorCode.PermissionDenied, (await rig.CallAsync("vacation.set", """[{"isEnabled":true,"textBody":"Away"}]""")).Error);
        Assert.Equal(requests, rig.Api.VacationSetRequests.Count);
        rig.ShapeSnapshot = null;
        Assert.True((await rig.CallAsync("vacation.set", """[{"isEnabled":false,"subject":"Quiet"}]""")).IsSuccess);
        Assert.Equal("Quiet", rig.Api.CurrentVacation.Subject, StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("""[{}]""")]
    [InlineData("""[{"isEnabled":"yes"}]""")]
    [InlineData("""[{"isEnabled":true,"extra":1}]""")]
    [InlineData("""[{"isEnabled":false,"subject":1}]""")]
    [InlineData("""[{"isEnabled":false,"fromDate":"2026-12-01"}]""")]
    [InlineData("""[{"isEnabled":false,"fromDate":"2026-12-01T00:00:00"}]""")]
    [InlineData("""[{"isEnabled":false,"fromDate":"2026-12-01T10:00:00Z"}]""")]
    [InlineData("""[{"isEnabled":false,"toDate":"2026-12-05T00:00:00Z"}]""")]
    [InlineData("""[{"isEnabled":false,"fromDate":"2026-12-05T00:00:00Z","toDate":"2026-12-01T23:59:59Z"}]""")]
    [InlineData("""[{"isEnabled":true}]""")]
    [InlineData("""[]""")]
    public async Task Vacation_set_refuses_input_the_reply_cannot_hold_and_writes_nothing(string args)
    {
        var rig = await StartAsync("vacation:write");
        var requests = rig.Api.VacationSetRequests.Count;
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("vacation.set", args)).Error);
        Assert.Equal(requests, rig.Api.VacationSetRequests.Count);
    }

    [Fact]
    public async Task Vacation_set_holds_the_subject_and_body_limits_at_their_edges()
    {
        var rig = await StartAsync("vacation:write");
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("vacation.set", $$"""[{"isEnabled":false,"subject":"{{new string('s', 501)}}"}]""")).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("vacation.set", $$"""[{"isEnabled":false,"textBody":"{{new string('t', 64_001)}}"}]""")).Error);
        Assert.True((await rig.CallAsync("vacation.set", $$"""[{"isEnabled":false,"subject":"{{new string('s', 500)}}","textBody":"{{new string('t', 64_000)}}"}]""")).IsSuccess);
        Assert.Equal(500, rig.Api.CurrentVacation.Subject!.Length);
    }

    [Fact]
    public async Task Vacation_set_returns_null_when_another_add_on_stops_the_change()
    {
        var rig = await StartAsync("vacation:write");
        using var stop = rig.Hooks.Intercept<VacationHookPayload>(HookNames.OnBeforeVacationUpdate, (_, _) => ValueTask.FromResult(false));
        var requests = rig.Api.VacationSetRequests.Count;
        var result = await rig.CallAsync("vacation.set", """[{"isEnabled":false,"subject":"X"}]""");
        Assert.True(result.IsSuccess);
        Assert.Equal(PluginValueKind.Null, result.Value!.Kind);
        Assert.Equal(requests, rig.Api.VacationSetRequests.Count);
    }

    [Fact]
    public async Task Vacation_set_is_a_conflict_when_the_reply_changed_after_it_was_read()
    {
        var rig = await StartAsync("vacation:write");
        // another client changes the filters script as the write goes out: the states the reply was read at are gone
        rig.OnWrite = () => rig.Api.SeedSieve("other", "# another client", false);
        var requests = rig.Api.VacationSetRequests.Count;
        Assert.Equal(PluginErrorCode.Conflict, (await rig.CallAsync("vacation.set", """[{"isEnabled":false,"subject":"X"}]""")).Error);
        Assert.NotEqual("X", rig.Api.CurrentVacation.Subject, StringComparer.Ordinal);
        Assert.Equal(requests, rig.Api.VacationSetRequests.Count);
    }

    [Fact]
    public async Task Vacation_set_before_a_stale_check_writes_nothing_and_during_it_is_an_unknown_outcome()
    {
        var rig = await StartAsync("vacation:write");
        rig.OnRead = () => rig.Active.Valid = false;
        Assert.Equal(PluginErrorCode.StaleContext, (await rig.CallAsync("vacation.set", """[{"isEnabled":false,"subject":"X"}]""")).Error);
        Assert.Empty(rig.Api.VacationSetRequests);
        var during = await StartAsync("vacation:write");
        during.OnWrite = () => during.Active.Valid = false;
        var result = await during.CallAsync("vacation.set", """[{"isEnabled":false,"subject":"X"}]""");
        Assert.Equal(PluginErrorCode.UnknownOutcome, result.Error);
        Assert.Null(result.Value);
        Assert.Equal("X", during.Api.CurrentVacation.Subject, StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("vacation.get", "[]", "vacation:write")]
    [InlineData("vacation.set", """[{"isEnabled":false}]""", "vacation:read")]
    [InlineData("identities.update", """["i7",{"name":"X"}]""", "identity:read")]
    [InlineData("accountData.get", "[]", "email:write")]
    [InlineData("accountData.set", """[null,1]""", "email:read")]
    [InlineData("notifications.show", """[{"title":"T"}]""", "ui:toast")]
    public async Task Each_method_is_denied_without_its_own_permission_and_reaches_no_server(string method, string args, string held)
    {
        var rig = await PluginRulesRig.StartAsync([held], Identities);
        var calls = rig.F.Server.ApiCalls.Count;
        Assert.Equal(PluginErrorCode.PermissionDenied, (await rig.CallAsync(method, args)).Error);
        Assert.Equal(calls, rig.F.Server.ApiCalls.Count);
        Assert.Empty(rig.Toasts);
    }

    // identities

    [Fact]
    public async Task Identities_update_changes_only_the_patched_fields_through_the_identity_service_and_cleans_the_signature()
    {
        var rig = await PluginRulesRig.StartAsync(["identity:write"], Identities);
        var result = await rig.CallAsync("identities.update", """["i7",{"name":"  Ada L.  ","replyTo":["Reply <reply@example.org>","plain@example.org"],"htmlSignature":"<b>Ada</b><script>x()</script>"}]""");
        Assert.True(result.IsSuccess);
        Assert.Equal(["bcc", "email", "htmlSignature", "id", "name", "replyTo", "textSignature"], PluginApiRig.Keys(result.Value!), StringComparer.Ordinal);
        Assert.Equal("Ada L.", result.Value!.Property("name").StringValue, StringComparer.Ordinal);
        Assert.Equal("reply@example.org, plain@example.org", result.Value.Property("replyTo").StringValue, StringComparer.Ordinal);
        Assert.Equal(PluginValueKind.Null, result.Value.Property("bcc").Kind);
        Assert.Equal("old text", result.Value.Property("textSignature").StringValue, StringComparer.Ordinal);
        Assert.Equal("ada@example.org", result.Value.Property("email").StringValue, StringComparer.Ordinal);
        Assert.DoesNotContain("script", result.Value.Property("htmlSignature").StringValue, StringComparison.OrdinalIgnoreCase);
        var patch = (JsonObject)rig.Api.IdentitySetRequests[^1]["update"]!["i7"]!;
        Assert.Equal(["htmlSignature", "name", "replyTo"], patch.Select(p => p.Key).Order(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
    }

    [Fact]
    public async Task Identities_update_clears_with_null_or_blank_text()
    {
        var rig = await PluginRulesRig.StartAsync(["identity:write"], Identities);
        var result = await rig.CallAsync("identities.update", """["i7",{"replyTo":null,"textSignature":"   "}]""");
        Assert.True(result.IsSuccess);
        Assert.Equal(PluginValueKind.Null, result.Value!.Property("replyTo").Kind);
        Assert.Equal("", result.Value.Property("textSignature").StringValue, StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("""["i7",{"email":"other@example.org"}]""")]
    [InlineData("""["i7",{}]""")]
    [InlineData("""["i7",{"bcc":["a@example.org"]}]""")]
    [InlineData("""["i7",{"name":1}]""")]
    [InlineData("""["i7",{"replyTo":"a@example.org"}]""")]
    [InlineData("""["i7",{"replyTo":["not an address"]}]""")]
    [InlineData("""["i7",{"replyTo":["\"Doe, John\" <a@example.org>"]}]""")]
    [InlineData("""["i7",{"replyTo":["a@example.org","a@example.org","a@example.org","a@example.org","a@example.org","a@example.org","a@example.org","a@example.org","a@example.org","a@example.org","a@example.org"]}]""")]
    [InlineData("""["i7",{"textSignature":5}]""")]
    [InlineData("""[1,{"name":"X"}]""")]
    [InlineData("""["i7"]""")]
    public async Task Identities_update_refuses_the_address_unknown_fields_and_bad_values_and_writes_nothing(string args)
    {
        var rig = await PluginRulesRig.StartAsync(["identity:write"], Identities);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("identities.update", args)).Error);
        Assert.Empty(rig.Api.IdentitySetRequests);
    }

    [Fact]
    public async Task Identities_update_holds_its_limits_and_refuses_an_unknown_identity()
    {
        var rig = await PluginRulesRig.StartAsync(["identity:write"], Identities);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("identities.update", $$"""["i7",{"name":"{{new string('n', 257)}}"}]""")).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("identities.update", $$"""["i7",{"textSignature":"{{new string('t', 20_001)}}"}]""")).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("identities.update", $$"""["i7",{"htmlSignature":"{{new string('h', 100_001)}}"}]""")).Error);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("identities.update", """["nope",{"name":"X"}]""")).Error);
        Assert.Empty(rig.Api.IdentitySetRequests);
        Assert.True((await rig.CallAsync("identities.update", $$"""["i7",{"name":"{{new string('n', 256)}}"}]""")).IsSuccess);
    }

    [Fact]
    public async Task Identities_update_returns_null_when_another_add_on_stops_it_and_is_stale_safe()
    {
        var rig = await PluginRulesRig.StartAsync(["identity:write"], Identities);
        using (rig.Hooks.Intercept<IdentityChangePayload>(HookNames.OnBeforeIdentityChange, (_, _) => ValueTask.FromResult(false)))
        {
            var stopped = await rig.CallAsync("identities.update", """["i7",{"name":"X"}]""");
            Assert.True(stopped.IsSuccess);
            Assert.Equal(PluginValueKind.Null, stopped.Value!.Kind);
        }

        Assert.Empty(rig.Api.IdentitySetRequests);
        rig.OnRead = () => rig.Active.Valid = false;
        Assert.Equal(PluginErrorCode.StaleContext, (await rig.CallAsync("identities.update", """["i7",{"name":"X"}]""")).Error);
        Assert.Empty(rig.Api.IdentitySetRequests);
        var during = await PluginRulesRig.StartAsync(["identity:write"], Identities);
        during.OnWrite = () => during.Active.Valid = false;
        var result = await during.CallAsync("identities.update", """["i7",{"name":"X"}]""");
        Assert.Equal(PluginErrorCode.UnknownOutcome, result.Error);
        Assert.Single(during.Api.IdentitySetRequests);
    }

    // accountData

    [Fact]
    public async Task Account_data_is_the_add_ons_own_entry_in_the_inbox_metadata_and_nothing_else_is_touched()
    {
        var rig = await StartAsync("email:read", "email:write");
        var inbox = rig.Folder("inbox");
        Assert.Null(rig.F.Server.Store.UpdateMailboxMetadata(inbox, new JsonObject { ["metadata/org.bulwarkmail.tags"] = "keep" }));
        var empty = (await rig.CallAsync("accountData.get", "[]")).Value!;
        Assert.Equal(["supported", "value"], PluginApiRig.Keys(empty), StringComparer.Ordinal);
        Assert.True(empty.Property("supported").BooleanValue);
        Assert.Equal(PluginValueKind.Null, empty.Property("value").Kind);
        Assert.True((await rig.SetDataAsync("""{"seen":[1,2,3],"label":"x"}""")).IsSuccess);
        var stored = rig.InboxMetadata;
        Assert.Equal("keep", (string?)stored["org.bulwarkmail.tags"]);
        Assert.Equal(3, stored["org.bulwarkmail.plugin.probe"]!["seen"]!.AsArray().Count);
        var read = (await rig.CallAsync("accountData.get", "[]")).Value!;
        Assert.Equal("x", read.Property("value").Property("label").StringValue, StringComparer.Ordinal);
        Assert.True((await rig.SetDataAsync("""null""")).IsSuccess);
        Assert.False(rig.InboxMetadata.ContainsKey("org.bulwarkmail.plugin.probe"));
        Assert.Equal("keep", (string?)rig.InboxMetadata["org.bulwarkmail.tags"]);
    }

    [Fact]
    public async Task Account_data_of_two_add_ons_is_isolated()
    {
        var rig = await StartAsync("email:read", "email:write");
        var other = rig.BindOther("other-addon", ["email:read", "email:write"]);
        Assert.True((await rig.SetDataAsync("\"mine\"")).IsSuccess);
        Assert.True((await rig.SetDataAsync("\"theirs\"", other)).IsSuccess);
        Assert.Equal("mine", (await rig.CallAsync("accountData.get", "[]")).Value!.Property("value").StringValue, StringComparer.Ordinal);
        Assert.Equal("theirs", (await rig.CallAsync(other, "accountData.get", "[]")).Value!.Property("value").StringValue, StringComparer.Ordinal);
        Assert.Equal("mine", (string?)rig.InboxMetadata["org.bulwarkmail.plugin.probe"]);
        Assert.Equal("theirs", (string?)rig.InboxMetadata["org.bulwarkmail.plugin.other-addon"]);
        // an add-on id that cannot be a metadata key holds no data
        var odd = rig.BindOther("Odd_Addon", ["email:read", "email:write"]);
        Assert.False((await rig.CallAsync(odd, "accountData.get", "[]")).Value!.Property("supported").BooleanValue);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.SetDataAsync("""1""", odd)).Error);
    }

    [Theory]
    [InlineData("""[null]""")]
    [InlineData("""[7,1]""")]
    [InlineData("""[]""")]
    public async Task Account_data_set_refuses_a_missing_value_or_an_account_that_is_not_an_id(string args)
    {
        var rig = await StartAsync("email:write");
        var calls = rig.F.Server.ApiCalls.Count;
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("accountData.set", args)).Error);
        Assert.Equal(calls, rig.F.Server.ApiCalls.Count);
    }

    [Fact]
    public async Task Account_data_set_holds_four_kilobytes_and_another_account_is_unavailable()
    {
        var rig = await StartAsync("email:read", "email:write");
        // the JSON of a string is two quotes around it: 4094 characters are 4096 bytes
        Assert.True((await rig.SetDataAsync("\"" + new string('a', 4094) + "\"")).IsSuccess);
        var before = rig.InboxMetadata["org.bulwarkmail.plugin.probe"]!.ToJsonString();
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.SetDataAsync("\"" + new string('a', 4095) + "\"")).Error);
        Assert.Equal(before, rig.InboxMetadata["org.bulwarkmail.plugin.probe"]!.ToJsonString());
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("accountData.get", """["someone-elses-account"]""")).Error);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("accountData.set", """["someone-elses-account","x"]""")).Error);
        var own = rig.F.Client.Session.MailAccountId;
        Assert.True((await rig.CallAsync("accountData.get", $"[\"{own}\"]")).IsSuccess);
    }

    [Fact]
    public async Task Account_data_without_server_metadata_is_unsupported_and_is_rate_limited()
    {
        var plain = await PluginRulesRig.StartAsync(["email:read", "email:write"], new FakeJmapOptions { SieveCapability = true, VacationCapability = true });
        var read = (await plain.CallAsync("accountData.get", "[]")).Value!;
        Assert.False(read.Property("supported").BooleanValue);
        Assert.Equal(PluginValueKind.Null, read.Property("value").Kind);
        Assert.Equal(PluginErrorCode.Unavailable, (await plain.SetDataAsync("""1""")).Error);

        var rig = await StartAsync("email:read", "email:write");
        for (var i = 0; i < 30; i++) Assert.True((await rig.CallAsync("accountData.get", "[]")).IsSuccess, "read " + i);
        Assert.Equal(PluginErrorCode.RateLimited, (await rig.CallAsync("accountData.get", "[]")).Error);
        for (var i = 0; i < 20; i++) Assert.True((await rig.SetDataAsync($"{i}")).IsSuccess, "write " + i);
        Assert.Equal(PluginErrorCode.RateLimited, (await rig.SetDataAsync("""99""")).Error);
    }

    [Fact]
    public async Task Account_data_set_before_a_stale_check_writes_nothing_and_during_it_is_an_unknown_outcome()
    {
        var rig = await StartAsync("email:write");
        rig.Active.Valid = false;
        Assert.Equal(PluginErrorCode.NoSession, (await rig.SetDataAsync("""1""")).Error);
        Assert.False(rig.InboxMetadata.ContainsKey("org.bulwarkmail.plugin.probe"));
        rig.Active.Valid = true;
        rig.F.Server.Api.BeforeMailboxSet = _ => rig.Active.Valid = false;
        var result = await rig.SetDataAsync("""1""");
        Assert.Equal(PluginErrorCode.UnknownOutcome, result.Error);
        Assert.True(rig.InboxMetadata.ContainsKey("org.bulwarkmail.plugin.probe"));
    }

    // plugin.getConfig

    [Fact]
    public async Task Get_config_returns_only_the_calling_add_ons_own_settings_and_needs_no_permission()
    {
        var rig = await StartAsync();
        rig.Config["probe"] = new Dictionary<string, JsonNode?>(StringComparer.Ordinal) { ["endpoint"] = "https://paperless.example.org", ["limit"] = 5, ["nested"] = new JsonObject { ["on"] = true }, ["none"] = null };
        rig.Config["other-addon"] = new Dictionary<string, JsonNode?>(StringComparer.Ordinal) { ["secretOfTheOther"] = "x" };
        var other = rig.BindOther("other-addon", []);
        var own = (await rig.CallAsync("plugin.getConfig", "[]")).Value!;
        Assert.Equal(["endpoint", "limit", "nested", "none"], PluginApiRig.Keys(own), StringComparer.Ordinal);
        Assert.Equal("https://paperless.example.org", own.Property("endpoint").StringValue, StringComparer.Ordinal);
        Assert.True(own.Property("nested").Property("on").BooleanValue);
        Assert.Equal(["secretOfTheOther"], PluginApiRig.Keys((await rig.CallAsync(other, "plugin.getConfig", "[]")).Value!), StringComparer.Ordinal);
        // an argument naming another add-on changes nothing
        Assert.Equal(["endpoint", "limit", "nested", "none"], PluginApiRig.Keys((await rig.CallAsync("plugin.getConfig", """["other-addon"]""")).Value!), StringComparer.Ordinal);
        var unknown = rig.BindOther("nobody", []);
        Assert.Equal(0, (await rig.CallAsync(unknown, "plugin.getConfig", "[]")).Value!.Count);
    }

    [Fact]
    public async Task Get_config_of_a_revoked_add_on_returns_nothing()
    {
        var rig = await StartAsync();
        rig.Config["probe"] = new Dictionary<string, JsonNode?>(StringComparer.Ordinal) { ["a"] = "b" };
        rig.Auth.Current = PluginApiRig.Grant([], enabled: false);
        var result = await rig.CallAsync("plugin.getConfig", "[]");
        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
    }

    // notifications.show

    [Fact]
    public async Task Notifications_show_shows_one_attributed_toast_on_one_line()
    {
        var rig = await StartAsync("ui:notifications");
        var result = await rig.CallAsync("notifications.show", """[{"title":"  Invoice\nreceived ","body":"From‮ ACME\r\nLtd","tag":"invoice:1"}]""");
        Assert.True(result.IsSuccess);
        Assert.Equal("toast", result.Value!.StringValue, StringComparer.Ordinal);
        var toast = Assert.Single(rig.Toasts);
        Assert.Equal("probe", toast.Plugin, StringComparer.Ordinal);
        Assert.Equal("Invoice received: From ACME Ltd", toast.Message, StringComparer.Ordinal);
        Assert.True((await rig.CallAsync("notifications.show", """[{"title":"Only a title"}]""")).IsSuccess);
        Assert.Equal("Only a title", rig.Toasts[1].Message, StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("""[{}]""")]
    [InlineData("""[{"title":""}]""")]
    [InlineData("""[{"title":"\n"}]""")]
    [InlineData("""[{"title":"T","extra":1}]""")]
    [InlineData("""[{"title":"T","tag":"has space"}]""")]
    [InlineData("""[{"title":"T","body":5}]""")]
    [InlineData("""[{"title":"T","action":{"label":"Open","url":"ftp://x.example.org"}}]""")]
    [InlineData("""[{"title":"T","action":{"label":"Open","url":"https://user:pw@x.example.org"}}]""")]
    [InlineData("""[{"title":"T","action":{"label":"","url":"https://x.example.org"}}]""")]
    [InlineData("""[{"title":"T","action":{"label":"Open"}}]""")]
    [InlineData("""[{"title":"T","action":{"label":"Open","url":"https://x.example.org","x":1}}]""")]
    [InlineData("""[{"title":"T","action":{"label":"Open","url":"https://not-approved.example.org/p"}}]""")]
    [InlineData("""[]""")]
    public async Task Notifications_show_refuses_bad_input_and_shows_nothing(string args)
    {
        var rig = await StartAsync("ui:notifications");
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("notifications.show", args)).Error);
        Assert.Empty(rig.Toasts);
    }

    [Fact]
    public async Task Notifications_show_holds_its_length_limits_at_their_edges()
    {
        var rig = await StartAsync("ui:notifications");
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("notifications.show", $$"""[{"title":"{{new string('t', 121)}}"}]""")).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("notifications.show", $$"""[{"title":"T","body":"{{new string('b', 301)}}"}]""")).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("notifications.show", $$$"""[{"title":"T","action":{"label":"{{{new string('l', 41)}}}","url":"https://x.example.org"}}]""")).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("notifications.show", $$"""[{"title":"T","tag":"{{new string('g', 65)}}"}]""")).Error);
        Assert.Empty(rig.Toasts);
        Assert.True((await rig.CallAsync("notifications.show", $$"""[{"title":"{{new string('t', 120)}}","body":"{{new string('b', 300)}}","tag":"{{new string('g', 64)}}"}]""")).IsSuccess);
    }

    [Fact]
    public async Task Notifications_show_is_limited_to_five_a_minute_for_each_add_on()
    {
        var rig = await StartAsync("ui:notifications");
        var other = rig.BindOther("other-addon", ["ui:notifications"]);
        for (var i = 0; i < 5; i++) Assert.True((await rig.CallAsync("notifications.show", """[{"title":"T"}]""")).IsSuccess, "call " + i);
        Assert.Equal(PluginErrorCode.RateLimited, (await rig.CallAsync("notifications.show", """[{"title":"T"}]""")).Error);
        Assert.Equal(5, rig.Toasts.Count);
        Assert.True((await rig.CallAsync(other, "notifications.show", """[{"title":"T"}]""")).IsSuccess);
        rig.Time.Advance(TimeSpan.FromSeconds(61));
        Assert.True((await rig.CallAsync("notifications.show", """[{"title":"T"}]""")).IsSuccess);
    }

    [Fact]
    public async Task Notifications_show_accepts_an_action_only_on_a_server_the_add_on_already_talks_to()
    {
        var rig = await StartAsync("ui:notifications", "http:fetch");
        rig.Auth.Current = new PluginGrantSnapshot(new PluginPackageIdentity("probe", "1.0.0", new string('a', 64)), 1, true, ["ui:notifications", "http:fetch"], ["a1"], ["https://paperless.example.org", "https://*.cloud.example.org"]);
        Assert.True((await rig.CallAsync("notifications.show", """[{"title":"T","action":{"label":"Open","url":"https://paperless.example.org/documents/1"}}]""")).IsSuccess);
        Assert.True((await rig.CallAsync("notifications.show", """[{"title":"T","action":{"label":"Open","url":"https://a.cloud.example.org/x"}}]""")).IsSuccess);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("notifications.show", """[{"title":"T","action":{"label":"Open","url":"https://a.b.cloud.example.org/x"}}]""")).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("notifications.show", """[{"title":"T","action":{"label":"Open","url":"http://paperless.example.org/x"}}]""")).Error);
        Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("notifications.show", """[{"title":"T","action":{"label":"Open","url":"https://evil.example.net/x"}}]""")).Error);
        Assert.Equal(2, rig.Toasts.Count);
    }

    [Fact]
    public async Task Notifications_show_of_a_revoked_add_on_shows_nothing()
    {
        var rig = await StartAsync("ui:notifications");
        rig.Auth.Current = PluginApiRig.Grant(["ui:notifications"], enabled: false);
        Assert.False((await rig.CallAsync("notifications.show", """[{"title":"T"}]""")).IsSuccess);
        Assert.Empty(rig.Toasts);
    }
}
