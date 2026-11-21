// Source: Bulwark lib/plugin-sandbox/host-api.ts:194,207-209,501-503 (user.findPeople directory:read, user.logout auth:emit, user.returnToOwnAccount auth:impersonation-return), host-api-account.ts:100-212 (findPeople: ids 1 to 20,
// text of 3 or more characters, 20 results, 10 lookups a minute, complete false when the directory cannot be listed in full, a 5 minute listing); Barbican fences: denial with a neighbouring permission reaches no service, a stale call
// gives no data, user.logout asks the user in the host dialog first and signs out only after a yes, user.returnToOwnAccount stays an unknown method.
#pragma warning disable MA0006, MA0002 // assertions compare string literals

using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Contacts;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Plugins.Api.Account;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Microsoft.Extensions.Time.Testing;

namespace Barbican.Core.Tests.Plugins.Api;

public sealed class PluginUserDirectoryTests
{
    private static readonly string[] PersonFields = ["id", "type", "name", "email"];

    private sealed class Directory : IContactDirectory
    {
        public bool Available { get; set; } = true;

        public bool Fallback { get; set; }

        public bool Fails { get; set; }

        public int Loads { get; private set; }

        public Action? OnLoad { get; set; }

        public List<JsonNode> Principals { get; } =
        [
            Person("p1", "individual", "ada", "Ada Lovelace", "ADA@example.org"),
            Person("p2", "individual", "grace", null, "grace@example.org"),
            Person("g1", "group", "engineers", "Engineers", "eng@example.org"),
        ];

        public static JsonObject Person(string id, string type, string name, string? description, string email) =>
            new JsonObject { ["id"] = id, ["type"] = type, ["name"] = name, ["description"] = description, ["email"] = email, ["accounts"] = new JsonObject { ["secret"] = true }, ["timeZone"] = "Europe/Berlin" };

        public bool IsAvailable(string accountKey) => Available;

        public Task<Result<(IReadOnlyList<JsonNode> Principals, bool Fallback)>> LoadAsync(string accountKey, CancellationToken ct)
        {
            Loads++;
            OnLoad?.Invoke();
            return Task.FromResult(Fails
                ? Result<(IReadOnlyList<JsonNode> Principals, bool Fallback)>.Fail(new ValidationError("no"))
                : Result<(IReadOnlyList<JsonNode> Principals, bool Fallback)>.Ok((Principals, Fallback)));
        }
    }

    private sealed class Maintenance : IAccountMaintenance
    {
        public List<string> SignedOut { get; } = [];

        public bool Fails { get; set; }

        public Action? OnSignedOut { get; set; }

        public Task<Result<Unit>> SignOutAsync(string accountKey, CancellationToken ct)
        {
            SignedOut.Add(accountKey);
            OnSignedOut?.Invoke();
            return Task.FromResult(Fails ? Result<Unit>.Fail(new ValidationError("no")) : Result<Unit>.Ok(default));
        }

        public Task<Result<Unit>> SignOutAllAsync(CancellationToken ct) => throw new NotSupportedException();

        public Task<Result<Unit>> SetDefaultAsync(string accountKey, CancellationToken ct) => throw new NotSupportedException();

        public Task<Result<Unit>> MoveAsync(string accountKey, int newIndex, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class Rig
    {
        public PluginApiRig Api { get; private set; } = null!;

        public Directory Dir { get; } = new();

        public Maintenance Out { get; } = new();

        public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 21, 9, 0, 0, TimeSpan.Zero));

        /// <summary>What the host dialog answers to user.logout: true accepted, false declined, null could not be shown.</summary>
        public bool? Answer { get; set; }

        public List<string> Asked { get; } = [];

        public Action? OnAsk { get; set; }

        public static async Task<Rig> StartAsync(params string[] permissions)
        {
            var rig = new Rig();
            rig.Api = await PluginApiRig.StartAsync(() =>
            [
                PluginAccountMethods.FindPeople(rig.Dir, new PluginMailRateLimiter(rig.Time), rig.Time),
                PluginAccountMethods.Logout(rig.Out, (login, _, _) =>
                {
                    rig.Asked.Add(login);
                    rig.OnAsk?.Invoke();
                    return Task.FromResult(rig.Answer);
                }),
            ], permissions);
            return rig;
        }

        public Task<PluginCallOutcome> CallAsync(string method, string args) => Api.CallAsync(method, args);
    }

    [Fact]
    public async Task FindPeople_searches_by_name_or_address_by_id_and_returns_exactly_four_fields()
    {
        var rig = await Rig.StartAsync("directory:read");
        var byName = (await rig.CallAsync("user.findPeople", """[{"text":"LOVE"}]""")).Value!;
        Assert.Equal(["complete", "people"], PluginApiRig.Keys(byName), StringComparer.Ordinal);
        Assert.True(byName.Property("complete").BooleanValue);
        var ada = Assert.Single(Enumerable.Range(0, byName.Property("people").Count).Select(i => byName.Property("people").Item(i)));
        Assert.Equal(["email", "id", "name", "type"], PluginApiRig.Keys(ada), StringComparer.Ordinal);
        Assert.Equal(["p1", "individual", "Ada Lovelace", "ada@example.org"], PersonFields.Select(n => ada.Property(n).StringValue), StringComparer.Ordinal);
        // the address is searched too, a principal without a description is shown by its name, accounts and time zones never come through
        var byAddress = (await rig.CallAsync("user.findPeople", """[{"text":"grace@"}]""")).Value!;
        Assert.Equal("grace", byAddress.Property("people").Item(0).Property("name").StringValue, StringComparer.Ordinal);
        var byId = (await rig.CallAsync("user.findPeople", """[{"ids":["g1","p2","missing"]}]""")).Value!;
        Assert.Equal(["g1", "p2"], Enumerable.Range(0, byId.Property("people").Count).Select(i => byId.Property("people").Item(i).Property("id").StringValue).Order(StringComparer.Ordinal), StringComparer.Ordinal);
        Assert.Contains(Enumerable.Range(0, byId.Property("people").Count).Select(i => byId.Property("people").Item(i)), p => p.Property("id").StringValue == "g1" && p.Property("type").StringValue == "group");
        Assert.Equal(1, (await rig.CallAsync("user.findPeople", """[{"ids":["p1","p2"],"text":"ada"}]""")).Value!.Property("people").Count);
        Assert.Equal(0, (await rig.CallAsync("user.findPeople", """[{"text":"zzzz"}]""")).Value!.Property("people").Count);
    }

    [Fact]
    public async Task FindPeople_returns_at_most_twenty_people_and_checks_every_argument()
    {
        var rig = await Rig.StartAsync("directory:read");
        for (var i = 0; i < 30; i++) rig.Dir.Principals.Add(Directory.Person("m" + i, "individual", "member" + i, "Member " + i, $"m{i}@example.org"));
        Assert.Equal(PluginUserFindPeople.MaxPeople, (await rig.CallAsync("user.findPeople", """[{"text":"member"}]""")).Value!.Property("people").Count);
        string[] bad =
        [
            "[]", "[5]", "[{}]", """[{"text":"ab"}]""", """[{"text":"  ab  "}]""", """[{"text":5}]""", """[{"ids":[]}]""", """[{"ids":"p1"}]""", """[{"ids":[5]}]""", """[{"ids":[""]}]""",
            "[{\"ids\":[" + string.Join(",", Enumerable.Range(0, 21).Select(i => $"\"i{i}\"")) + "]}]", "[{\"text\":\"" + new string('a', 201) + "\"}]", """[{"text":"abc","extra":1}]""", """[{"text":"abc","accountId":5}]""",
        ];
        foreach (var args in bad) Assert.Equal(PluginErrorCode.InvalidArguments, (await rig.CallAsync("user.findPeople", args)).Error);
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("user.findPeople", """[{"text":"abc","accountId":"stranger"}]""")).Error);
        Assert.Equal(1, rig.Dir.Loads);
        Assert.True((await rig.CallAsync("user.findPeople", $$"""[{"text":"abc","accountId":"{{rig.Api.F.Client.Session.MailAccountId}}"}]""")).IsSuccess);
    }

    [Fact]
    public async Task FindPeople_is_denied_with_a_neighbouring_permission_and_reaches_no_directory()
    {
        var rig = await Rig.StartAsync("account:read", "contacts:read");
        var calls = rig.Api.F.Server.ApiCalls.Count;
        Assert.Equal(PluginErrorCode.PermissionDenied, (await rig.CallAsync("user.findPeople", """[{"text":"ada"}]""")).Error);
        Assert.Equal(0, rig.Dir.Loads);
        Assert.Equal(calls, rig.Api.F.Server.ApiCalls.Count);
    }

    [Fact]
    public async Task FindPeople_says_incomplete_when_the_server_does_not_list_its_directory_and_does_not_keep_a_failed_listing()
    {
        var rig = await Rig.StartAsync("directory:read");
        rig.Dir.Fallback = true;
        Assert.False((await rig.CallAsync("user.findPeople", """[{"text":"ada"}]""")).Value!.Property("complete").BooleanValue);
        rig.Time.Advance(PluginDirectoryCache.Lifetime + TimeSpan.FromSeconds(1));
        rig.Dir.Fallback = false;
        rig.Dir.Fails = true;
        var failed = (await rig.CallAsync("user.findPeople", """[{"text":"ada"}]""")).Value!;
        Assert.False(failed.Property("complete").BooleanValue);
        Assert.Equal(0, failed.Property("people").Count);
        rig.Dir.Fails = false;
        Assert.Equal(1, (await rig.CallAsync("user.findPeople", """[{"text":"ada"}]""")).Value!.Property("people").Count);
        rig.Dir.Available = false;
        rig.Time.Advance(PluginDirectoryCache.Lifetime + TimeSpan.FromSeconds(1));
        var none = (await rig.CallAsync("user.findPeople", """[{"text":"ada"}]""")).Value!;
        Assert.Equal(0, none.Property("people").Count);
        Assert.False(none.Property("complete").BooleanValue);
    }

    [Fact]
    public async Task FindPeople_keeps_the_listing_five_minutes_and_allows_ten_lookups_a_minute()
    {
        var rig = await Rig.StartAsync("directory:read");
        for (var i = 0; i < 10; i++) Assert.True((await rig.CallAsync("user.findPeople", """[{"text":"ada"}]""")).IsSuccess);
        Assert.Equal(1, rig.Dir.Loads);
        Assert.Equal(PluginErrorCode.RateLimited, (await rig.CallAsync("user.findPeople", """[{"text":"ada"}]""")).Error);
        rig.Time.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        Assert.True((await rig.CallAsync("user.findPeople", """[{"text":"ada"}]""")).IsSuccess);
        Assert.Equal(2, rig.Dir.Loads);
    }

    [Fact]
    public async Task FindPeople_gives_nothing_when_the_call_goes_stale_while_the_directory_loads()
    {
        var rig = await Rig.StartAsync("directory:read");
        rig.Api.Active.Valid = false;
        Assert.Equal(PluginErrorCode.NoSession, (await rig.CallAsync("user.findPeople", """[{"text":"ada"}]""")).Error);
        Assert.Equal(0, rig.Dir.Loads);
        rig.Api.Active.Valid = true;
        rig.Dir.OnLoad = () => rig.Api.Active.Valid = false;
        Assert.Equal(PluginErrorCode.StaleContext, (await rig.CallAsync("user.findPeople", """[{"text":"ada"}]""")).Error);
    }

    [Fact]
    public async Task Logout_signs_the_login_out_only_after_the_user_accepted_the_host_dialog()
    {
        var rig = await Rig.StartAsync("auth:emit");
        rig.Answer = true;
        var result = await rig.CallAsync("user.logout", "[]");
        Assert.True(result.IsSuccess);
        Assert.Equal(["user@example.org"], rig.Asked, StringComparer.Ordinal);
        Assert.Equal(["a1"], rig.Out.SignedOut, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Logout_does_nothing_when_the_user_declines_or_the_dialog_cannot_be_shown()
    {
        var rig = await Rig.StartAsync("auth:emit");
        rig.Answer = false;
        Assert.Equal(PluginErrorCode.Cancelled, (await rig.CallAsync("user.logout", "[]")).Error);
        rig.Answer = null;
        Assert.Equal(PluginErrorCode.Unavailable, (await rig.CallAsync("user.logout", "[]")).Error);
        Assert.Empty(rig.Out.SignedOut);
        Assert.Equal(2, rig.Asked.Count);
    }

    [Fact]
    public async Task Logout_is_denied_with_a_neighbouring_permission_without_asking_or_signing_out()
    {
        var rig = await Rig.StartAsync("account:read", "auth:impersonation-return");
        rig.Answer = true;
        Assert.Equal(PluginErrorCode.PermissionDenied, (await rig.CallAsync("user.logout", "[]")).Error);
        Assert.Empty(rig.Asked);
        Assert.Empty(rig.Out.SignedOut);
    }

    [Fact]
    public async Task Logout_signs_nothing_out_when_the_call_goes_stale_while_the_user_decides_and_reports_a_failed_sign_out()
    {
        var rig = await Rig.StartAsync("auth:emit");
        rig.Answer = true;
        rig.OnAsk = () => rig.Api.Auth.Current = PluginApiRig.Grant(["auth:emit"], enabled: false);
        Assert.Equal(PluginErrorCode.StaleContext, (await rig.CallAsync("user.logout", "[]")).Error);
        Assert.Empty(rig.Out.SignedOut);
        rig.OnAsk = null;
        rig.Api.Auth.Current = PluginApiRig.Grant(["auth:emit"]);
        rig.Out.Fails = true;
        Assert.Equal(PluginErrorCode.Failed, (await rig.CallAsync("user.logout", "[]")).Error);
        // a sign-out that ends the add-on's own authority cannot be answered: the dispatcher reports an unknown outcome
        rig.Out.Fails = false;
        rig.Out.OnSignedOut = () => rig.Api.Active.Valid = false;
        Assert.Equal(PluginErrorCode.UnknownOutcome, (await rig.CallAsync("user.logout", "[]")).Error);
        Assert.Equal(2, rig.Out.SignedOut.Count);
    }

    [Fact]
    public async Task ReturnToOwnAccount_stays_an_unknown_method_even_with_its_permission()
    {
        var rig = await Rig.StartAsync("auth:impersonation-return", "auth:emit", "directory:read");
        Assert.Equal(PluginErrorCode.UnknownMethod, (await rig.CallAsync("user.returnToOwnAccount", "[]")).Error);
    }
}
