// Source: Bulwark lib/plugin-sandbox/host-api.ts:188-202 (permissions: account:read for user.getAccounts / getSelf / getMailAccounts, identity:read for getIdentities, none for
// user.getTimeZone and app.getInfo), host-api-account.ts:47-91, host-api.ts:475-499,1997-2015; Barbican fences (grant per login, stale session, revoked add-on) over FakeJmap.
using Barbican.Core.Accounts;
using Barbican.Core.Plugins.Api.Account;
using Barbican.Core.Plugins.Api.App;
using Barbican.Core.Plugins.Bridge;
using Barbican.FakeJmap;
using Microsoft.Extensions.Time.Testing;

namespace Barbican.Core.Tests.Plugins.Api;

public sealed class PluginAccountMethodsTests
{
    private sealed class Info : IPluginAppInfo
    {
        public string Name => "Barbican";

        public string Version => "9.9.9";
    }

    private static Task<PluginApiRig> StartAsync(string[] permissions, FakePrefs? prefs = null, FakeAccountManager? accounts = null, FakeJmapOptions? options = null, FakeTimeProvider? time = null)
    {
        var p = prefs ?? new FakePrefs();
        var a = accounts ?? new FakeAccountManager([new AccountInfo("a1", "user@example.org", "https://jmap.example.org")], "a1");
        return PluginApiRig.StartAsync(() => [.. PluginAccountMethods.Create(a, p, time ?? new FakeTimeProvider()), new PluginAppGetInfo(new Info())], permissions, options);
    }

    [Fact]
    public async Task Get_self_returns_exactly_the_account_id_address_and_name()
    {
        var rig = await StartAsync(["account:read"]);
        var result = await rig.CallAsync("user.getSelf", "[]");
        Assert.True(result.IsSuccess);
        Assert.Equal(["accountId", "email", "name"], PluginApiRig.Keys(result.Value!), StringComparer.Ordinal);
        Assert.Equal(rig.F.Client.Session.MailAccountId, result.Value!.Property("accountId").StringValue, StringComparer.Ordinal);
        Assert.Equal(rig.F.Client.Session.Username, result.Value.Property("email").StringValue, StringComparer.Ordinal);
        Assert.Equal("Test User", result.Value.Property("name").StringValue, StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("user.getSelf", "identity:read")]
    [InlineData("user.getAccounts", "email:read")]
    [InlineData("user.getMailAccounts", "settings:read")]
    [InlineData("user.getIdentities", "account:read")]
    public async Task A_method_is_denied_without_its_own_permission_and_reaches_no_server(string method, string held)
    {
        var rig = await StartAsync([held]);
        var before = rig.F.Server.ApiCalls.Count;
        Assert.Equal(PluginErrorCode.PermissionDenied, (await rig.CallAsync(method, "[]")).Error);
        Assert.Equal(before, rig.F.Server.ApiCalls.Count);
    }

    [Fact]
    public async Task Get_accounts_lists_only_the_logins_the_grant_names_without_secrets()
    {
        var accounts = new FakeAccountManager([new AccountInfo("a1", "user@example.org", "https://jmap.example.org"), new AccountInfo("a2", "other@example.net", "https://other.example.net")], "a1");
        var rig = await StartAsync(["account:read"], accounts: accounts);
        var list = (await rig.CallAsync("user.getAccounts", "[]")).Value!;
        Assert.Equal(1, list.Count);
        Assert.Equal(["avatarColor", "displayName", "email", "id", "isActive", "isConnected", "isDefault", "label", "serverUrl", "username"], PluginApiRig.Keys(list.Item(0)), StringComparer.Ordinal);
        Assert.Equal("a1", list.Item(0).Property("id").StringValue, StringComparer.Ordinal);
        Assert.True(list.Item(0).Property("isActive").BooleanValue);
        Assert.DoesNotContain("example.net", list.Item(0).ToJson().GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_mail_accounts_marks_the_own_account_personal_and_others_shared()
    {
        var rig = await StartAsync(["account:read"], options: new FakeJmapOptions { SharedAccounts = ["g1"] });
        var list = (await rig.CallAsync("user.getMailAccounts", "[]")).Value!;
        Assert.Equal(2, list.Count);
        Assert.Equal(["accountId", "isOpen", "isReadOnly", "kind", "name"], PluginApiRig.Keys(list.Item(0)), StringComparer.Ordinal);
        var own = Enumerable.Range(0, 2).Select(list.Item).Single(x => string.Equals(x.Property("kind").StringValue, "personal", StringComparison.Ordinal));
        var shared = Enumerable.Range(0, 2).Select(list.Item).Single(x => string.Equals(x.Property("kind").StringValue, "shared", StringComparison.Ordinal));
        Assert.True(own.Property("isOpen").BooleanValue);
        Assert.Equal("g1", shared.Property("accountId").StringValue, StringComparer.Ordinal);
        Assert.False(shared.Property("isOpen").BooleanValue);
    }

    [Fact]
    public async Task Get_identities_projects_named_fields_only()
    {
        var rig = await StartAsync(["identity:read"], options: new FakeJmapOptions { Identities = [new FakeIdentity("i7", "Ada", "ada@example.org") { ReplyTo = "reply@example.org", HtmlSignature = "<b>Ada</b>", TextSignature = "Ada" }] });
        var list = (await rig.CallAsync("user.getIdentities", "[]")).Value!;
        Assert.Equal(1, list.Count);
        var identity = list.Item(0);
        Assert.Empty(PluginApiRig.Keys(identity).Except(["id", "name", "email", "replyTo", "bcc", "textSignature", "htmlSignature", "mayDelete"], StringComparer.Ordinal));
        Assert.Equal("i7", identity.Property("id").StringValue, StringComparer.Ordinal);
        Assert.Equal("<b>Ada</b>", identity.Property("htmlSignature").StringValue, StringComparer.Ordinal);
        Assert.Equal("reply@example.org", identity.Property("replyTo").Item(0).Property("email").StringValue, StringComparer.Ordinal);
        Assert.False(identity.Property("mayDelete").BooleanValue);
    }

    [Fact]
    public async Task Time_zone_needs_no_permission_and_prefers_the_setting()
    {
        var prefs = new FakePrefs();
        var rig = await StartAsync([], prefs);
        Assert.Contains("UTC", (await rig.CallAsync("user.getTimeZone", "[]")).Value!.StringValue, StringComparison.Ordinal);
        prefs.Put("timeZone", "Europe/Berlin");
        Assert.Equal("Europe/Berlin", (await rig.CallAsync("user.getTimeZone", "[]")).Value!.StringValue, StringComparer.Ordinal);
        prefs.Put("timeZone", "auto");
        Assert.Contains("UTC", (await rig.CallAsync("user.getTimeZone", "[]")).Value!.StringValue, StringComparison.Ordinal);
        prefs.Put("timeZone", "../../etc");
        Assert.Contains("UTC", (await rig.CallAsync("user.getTimeZone", "[]")).Value!.StringValue, StringComparison.Ordinal);
    }

    [Fact]
    public async Task App_info_needs_no_permission_and_has_exactly_six_fields()
    {
        var rig = await StartAsync([]);
        var info = (await rig.CallAsync("app.getInfo", "[]")).Value!;
        Assert.Equal(["demoMode", "edition", "name", "product", "stalwartFeatures", "version"], PluginApiRig.Keys(info), StringComparer.Ordinal);
        Assert.Equal("Barbican", info.Property("product").StringValue, StringComparer.Ordinal);
        Assert.Equal("9.9.9", info.Property("version").StringValue, StringComparer.Ordinal);
        Assert.False(info.Property("demoMode").BooleanValue);
    }

    [Fact]
    public void The_app_version_never_carries_build_metadata_or_a_path()
    {
        var version = new PluginAppInfo().Version;
        Assert.DoesNotContain('+', version);
        Assert.DoesNotContain('\\', version);
    }

    [Fact]
    public async Task A_stale_session_or_a_grant_that_changes_during_the_server_call_returns_no_data()
    {
        var rig = await StartAsync(["account:read", "identity:read"]);
        rig.Active.Valid = false;
        Assert.Equal(PluginErrorCode.NoSession, (await rig.CallAsync("user.getSelf", "[]")).Error);
        rig.Active.Valid = true;
        rig.OnMailCall = () => rig.Active.Valid = false;
        var result = await rig.CallAsync("user.getIdentities", "[]");
        Assert.Equal(PluginErrorCode.StaleContext, result.Error);
        Assert.Null(result.Value);
        rig.Active.Valid = true;
        rig.OnMailCall = () => rig.Auth.Current = PluginApiRig.Grant(["account:read"], enabled: false);
        Assert.False((await rig.CallAsync("user.getSelf", "[]")).IsSuccess);
    }

    [Fact]
    public async Task Logout_return_to_own_account_and_find_people_stay_unknown()
    {
        var rig = await StartAsync(["account:read", "directory:read"]);
        foreach (var method in new[] { "user.logout", "user.returnToOwnAccount", "user.findPeople" })
        {
            Assert.Equal(PluginErrorCode.UnknownMethod, (await rig.CallAsync(method, "[]")).Error);
        }
    }
}
