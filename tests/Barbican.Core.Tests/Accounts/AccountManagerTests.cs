// Rust main.rs: on_login (:753; a one-time code is never remembered), restore_session (:430), on_switch_account (:816; a failed switch keeps
// the current account), on_add_account (:838); Bulwark stores/account-store.ts (several accounts signed in at once, the default star) and
// lib/account-inbox-unread.ts (the inbox unread of the accounts not on screen). spec I-001..I-010 (data side), J-020 hooks.

using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Auth;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Transport;
using Barbican.Core.Mail.Service;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Tests.Auth;
using Barbican.Core.Tests.Jmap;
using Barbican.Core.Tests.Settings.Prefs;
using Barbican.FakeJmap;

namespace Barbican.Core.Tests.Accounts;

public sealed partial class AccountManagerTests : IAsyncDisposable
{
    private const string OriginA = "https://jmap.example.org";
    private const string OriginB = "https://mail.example.net";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "barbican-accounts-" + Guid.NewGuid().ToString("N"));
    private readonly FakeJmapServer _a = new(new FakeJmapOptions { Origin = OriginA, Username = "ana@example.org", Password = "pw-a", Otp = null });
    private readonly FakeJmapServer _b = new(new FakeJmapOptions { Origin = OriginB, Username = "ben@example.net", Password = "pw-b" });
    private readonly FakeOAuthProvider _oauthA;
    private readonly JmapHttp _http;
    private readonly MemoryVault _vault = new();
    private readonly AccountStore _store;
    private readonly RecordingHooks _hooks = new();
    private readonly OAuthClientRegistry _clients;
    private readonly AccountManager _manager;

    public AccountManagerTests()
    {
        _oauthA = new FakeOAuthProvider(_a);
        _http = new JmapHttp(() => new OriginRouter((OriginA, _oauthA), (OriginB, _b)));
        _store = new AccountStore(_vault);
        var time = TimeProvider.System;
        var paths = new TestProfilePaths(_root);
        var oauthHttp = new OAuthHttp(_http, time);
        _clients = new OAuthClientRegistry(oauthHttp, paths);
        var flow = new OAuthFlow(new OAuthDiscovery(oauthHttp), _clients, oauthHttp, new RedirectFollowingLauncher(_http), new OAuthFlowOptions { LoopbackPort = 0 }, time);
        _manager = new AccountManager(new JmapClientFactory(_http, time), new MailAccountFactory(_hooks, time), _store, flow, oauthHttp, paths,
            ReadOnlyPolicy.AllReadOnly, _hooks, time, signOutCleanup: _signOutCleanup);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        await _manager.DisposeAsync();
        _store.Dispose();
        _clients.Dispose();
        _http.Dispose();
        _oauthA.Dispose();
        _a.Dispose();
        _b.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private Task<Barbican.Core.Common.Result<AccountInfo>> SignInAAsync(bool remember = true) =>
        _manager.SignInWithPasswordAsync(new PasswordSignIn(OriginA, "ana@example.org", "pw-a", null, remember), Ct);

    private Task<Barbican.Core.Common.Result<AccountInfo>> SignInBAsync() =>
        _manager.SignInWithPasswordAsync(new PasswordSignIn(OriginB, "ben@example.net", "pw-b", null, true), Ct);

    [Fact]
    public async Task A_password_sign_in_connects_remembers_and_tells_plugins()
    {
        var r = await SignInAAsync();
        Assert.True(r.IsOk, r.Error?.Message);
        Assert.Equal("ana@example.org", _manager.Active!.User);
        Assert.True(_manager.Active.IsReadOnly);
        Assert.NotNull(_manager.SessionFor(r.Value!.Key));

        var stored = Assert.Single(_vault.Accounts);
        Assert.Equal(StoredAccount.PasswordKind, stored.Kind);
        Assert.Equal("pw-a", stored.Password);
        Assert.True(stored.IsDefault);
        Assert.DoesNotContain("pw-a", stored.ToString(), StringComparison.Ordinal);

        var seen = _hooks.Seen.Select(s => s.Hook).ToList();
        Assert.Contains(HookNames.OnLogin, seen, StringComparer.Ordinal);
        Assert.Contains(HookNames.OnAuthReady, seen, StringComparer.Ordinal);

        // the first account is a sign-in, not an added account (Bulwark fires onAccountAdd from "Add account" only)
        Assert.DoesNotContain(HookNames.OnAccountAdd, seen, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Not_remembered_means_not_stored()
    {
        Assert.True((await SignInAAsync(remember: false)).IsOk);
        Assert.Empty(_vault.Accounts);
    }

    [Fact]
    public async Task A_wrong_password_signs_in_nobody()
    {
        var r = await _manager.SignInWithPasswordAsync(new PasswordSignIn(OriginA, "ana@example.org", "wrong", null, true), Ct);
        Assert.IsType<AuthError>(r.Error);
        Assert.Null(_manager.Active);
        Assert.Empty(_vault.Accounts);
    }

    [Fact]
    public async Task Empty_fields_are_refused_before_any_request()
    {
        Assert.Equal("Enter your server address.", (await _manager.SignInWithPasswordAsync(new PasswordSignIn("", "u", "p", null, true), Ct)).Error!.Message);
        Assert.Equal("Enter your username and password.", (await _manager.SignInWithPasswordAsync(new PasswordSignIn(OriginA, " ", "p", null, true), Ct)).Error!.Message);
        Assert.Empty(_a.Requests);
    }

    [Fact]
    public async Task A_one_time_code_is_asked_for_used_and_never_stored()
    {
        await using var otp = new OtpRig();
        var without = await otp.Manager.SignInWithPasswordAsync(new PasswordSignIn(OriginA, "ana@example.org", "pw-a", null, true), Ct);
        Assert.IsType<OtpRequiredError>(without.Error);

        var with = await otp.Manager.SignInWithPasswordAsync(new PasswordSignIn(OriginA, "ana@example.org", "pw-a", " 123456 ", true), Ct);
        Assert.True(with.IsOk, with.Error?.Message);
        Assert.Empty(otp.Vault.Accounts);
    }

    [Fact]
    public async Task Two_accounts_stay_signed_in_and_switching_keeps_both()
    {
        var a = (await SignInAAsync()).Value!;
        var b = (await SignInBAsync()).Value!;
        Assert.Equal(b.Key, _manager.Active!.Key);
        Assert.Equal(2, _manager.Accounts.Count);
        Assert.Contains(_hooks.Seen, s => string.Equals(s.Hook, HookNames.OnAccountAdd, StringComparison.Ordinal));

        var switched = await _manager.SwitchToAsync(a.Key, Ct);
        Assert.True(switched.IsOk, switched.Error?.Message);
        Assert.Equal(a.Key, _manager.Active!.Key);
        Assert.NotNull(_manager.SessionFor(b.Key));
        Assert.Contains(_hooks.Seen, s => string.Equals(s.Hook, HookNames.OnAccountSwitch, StringComparison.Ordinal));

        // most recently used first; the default stays the first account added
        Assert.Equal(a.Key, _vault.Accounts[0].Key);
        Assert.True(_vault.Accounts.Single(s => string.Equals(s.Key, a.Key, StringComparison.Ordinal)).IsDefault);
        var statuses = _manager.Statuses;
        Assert.Single(statuses, s => s.IsActive);
        Assert.Single(statuses, s => s.IsDefault);
    }

    [Fact]
    public async Task A_failed_switch_keeps_the_current_account()
    {
        await _vault.SaveAsync(
        [
            new StoredAccount(StoredAccount.PasswordKind, OriginA, "ana@example.org") { Password = "pw-a", IsDefault = true },
            new StoredAccount(StoredAccount.PasswordKind, OriginB, "ben@example.net") { Password = "changed-elsewhere" },
        ], Ct);
        var restored = await _manager.RestoreAsync(Ct);
        Assert.Equal("ana@example.org", restored!.User);

        var bKey = AccountKey.Of(OriginB, "ben@example.net");
        var r = await _manager.SwitchToAsync(bKey, Ct);
        Assert.StartsWith("Could not switch to ben@example.net", r.Error!.Message, StringComparison.Ordinal);
        Assert.Equal("ana@example.org", _manager.Active!.User);
    }

    [Fact]
    public async Task Restore_activates_the_most_recent_and_counts_the_others_unread()
    {
        await _vault.SaveAsync(
        [
            new StoredAccount(StoredAccount.PasswordKind, OriginB, "ben@example.net") { Password = "pw-b" },
            new StoredAccount(StoredAccount.PasswordKind, OriginA, "ana@example.org") { Password = "pw-a", IsDefault = true },
        ], Ct);
        var restored = await _manager.RestoreAsync(Ct);
        Assert.Equal("ben@example.net", restored!.User);

        var aKey = AccountKey.Of(OriginA, "ana@example.org");
        await Poll.UntilAsync(() => _manager.SessionFor(aKey) is not null, "the second account to connect");
        await _manager.RefreshUnreadAsync(Ct);
        var other = _manager.Statuses.Single(s => string.Equals(s.Account.Key, aKey, StringComparison.Ordinal));
        Assert.False(other.IsActive);
        Assert.True(other.IsConnected);
        Assert.NotNull(other.InboxUnread);
        Assert.True(other.IsDefault);
    }

    [Fact]
    public async Task Nothing_remembered_restores_nothing()
    {
        Assert.Null(await _manager.RestoreAsync(Ct));
        Assert.Null(_manager.Active);
    }

    [Fact]
    public async Task An_oauth_sign_in_stores_the_refresh_token_not_the_access_token()
    {
        var r = await _manager.SignInWithOAuthAsync(new OAuthSignIn(OriginA, "barbican", true) { LoginHint = "ana@example.org" }, Ct);
        Assert.True(r.IsOk, r.Error?.Message);
        Assert.Equal(StoredAccount.OAuthKind, r.Value!.Kind);
        var stored = Assert.Single(_vault.Accounts);
        Assert.True(stored.IsOAuth);
        Assert.True(_oauthA.IsValidRefresh(stored.Refresh!));
        Assert.Null(stored.Password);
        Assert.Equal(OriginA + "/oauth/token", stored.Endpoint);
        Assert.Equal("ana@example.org", stored.User);
    }

    [Fact]
    public async Task An_oauth_login_that_is_not_writable_gets_a_read_only_client_whatever_the_hint()
    {
        // the hint names a writable login, the server signs in another one: the client must end up read-only
        var paths = new TestProfilePaths(_root);
        var oauthHttp = new OAuthHttp(_http, TimeProvider.System);
        var flow = new OAuthFlow(new OAuthDiscovery(oauthHttp), _clients, oauthHttp, new RedirectFollowingLauncher(_http), new OAuthFlowOptions { LoopbackPort = 0 }, TimeProvider.System);
        await using var manager = new AccountManager(new JmapClientFactory(_http, TimeProvider.System), new MailAccountFactory(_hooks, TimeProvider.System), _store, flow, oauthHttp, paths, new ReadOnlyPolicy(["hint@example.org"]), _hooks, TimeProvider.System);

        var r = await manager.SignInWithOAuthAsync(new OAuthSignIn(OriginA, "barbican", false) { LoginHint = "hint@example.org" }, Ct);
        Assert.True(r.IsOk, r.Error?.Message);
        Assert.Equal("ana@example.org", r.Value!.User);
        Assert.True(r.Value.IsReadOnly);
        Assert.True(manager.SessionFor(r.Value.Key)!.Client.IsReadOnly);
    }

    [Fact]
    public async Task A_connect_that_fails_at_once_can_be_retried()
    {
        // a factory whose first answer is already complete when it returns (no await inside): the failed attempt must not stay pinned
        var paths = new TestProfilePaths(_root);
        var oauthHttp = new OAuthHttp(_http, TimeProvider.System);
        var flow = new OAuthFlow(new OAuthDiscovery(oauthHttp), _clients, oauthHttp, new RedirectFollowingLauncher(_http), new OAuthFlowOptions { LoopbackPort = 0 }, TimeProvider.System);
        var factory = new FailFirstFactory(new JmapClientFactory(_http, TimeProvider.System), failures: 2);
        await using var manager = new AccountManager(factory, new MailAccountFactory(_hooks, TimeProvider.System), _store, flow, oauthHttp, paths, ReadOnlyPolicy.AllReadOnly, _hooks, TimeProvider.System);
        await _vault.SaveAsync(
        [
            new StoredAccount(StoredAccount.PasswordKind, OriginA, "ana@example.org") { Password = "pw-a", IsDefault = true },
            new StoredAccount(StoredAccount.PasswordKind, OriginB, "ben@example.net") { Password = "pw-b" },
        ], Ct);

        // both accounts fail at once with a network error: the most recent one is on screen, connecting (Fable review 2 R2-02)
        var restored = await manager.RestoreAsync(Ct);
        Assert.Equal("ana@example.org", restored!.User);
        var aKey = AccountKey.Of(OriginA, "ana@example.org");
        var bKey = AccountKey.Of(OriginB, "ben@example.net");
        await Poll.UntilAsync(() => manager.SessionFor(bKey) is not null || factory.Calls >= 2, "the second account's attempt");

        var switched = await manager.SwitchToAsync(aKey, Ct);
        Assert.True(switched.IsOk, switched.Error?.Message);
        Assert.Equal("ana@example.org", manager.Active!.User);
    }

    [Fact]
    public void The_read_only_policy_lists_writable_accounts()
    {
        var policy = new ReadOnlyPolicy(["ana@example.org"]);
        Assert.False(policy.IsReadOnly("ANA@example.org", storedFlag: false));
        Assert.True(policy.IsReadOnly("ana@example.org", storedFlag: true));
        Assert.True(policy.IsReadOnly("ben@example.net", storedFlag: false));
        Assert.True(ReadOnlyPolicy.AllReadOnly.IsReadOnly("ana@example.org", storedFlag: false));
    }

    [Theory]
    [InlineData("smoke")]
    [InlineData("xrun-auto")]
    [InlineData("auto-4242")]
    public async Task An_agent_profile_without_the_allow_list_refuses_Email_set(string profile)
    {
        // F-12: an inherited BARBICAN_WRITABLE_ACCOUNTS (the M1 variable, gone) counts for nothing in the agents' profiles; only the .local lists open an account
        var env = new Dictionary<string, string?>(StringComparer.Ordinal) { ["BARBICAN_WRITABLE_ACCOUNTS"] = "ana@example.org" };
        var policy = ReadOnlyPolicy.For(profile, n => env.GetValueOrDefault(n), _ => "ana@example.org");
        Assert.Equal(0, policy.WritableCount);

        var paths = new TestProfilePaths(_root);
        var oauthHttp = new OAuthHttp(_http, TimeProvider.System);
        var flow = new OAuthFlow(new OAuthDiscovery(oauthHttp), _clients, oauthHttp, new RedirectFollowingLauncher(_http), new OAuthFlowOptions { LoopbackPort = 0 }, TimeProvider.System);
        await using var manager = new AccountManager(new JmapClientFactory(_http, TimeProvider.System), new MailAccountFactory(_hooks, TimeProvider.System), _store, flow, oauthHttp, paths, policy, _hooks, TimeProvider.System);
        var r = await manager.SignInWithPasswordAsync(new PasswordSignIn(OriginA, "ana@example.org", "pw-a", null, false), Ct);
        Assert.True(r.IsOk, r.Error?.Message);
        var client = manager.SessionFor(r.Value!.Key)!.Client;
        var set = await client.SendAsync(JmapRequest.Of(JmapRequest.Call("Email/set", new JsonObject { ["accountId"] = "b", ["destroy"] = new JsonArray("x") }, "w")), Ct);
        Assert.Equal("Email/set", Assert.IsType<ReadOnlyViolation>(set.Error).Method);
        Assert.DoesNotContain(_a.ApiCalls, c => c.Contains("Email/set", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("dev")]
    [InlineData("main")]
    public async Task The_users_own_profile_writes_to_every_account_even_one_flagged_read_only(string profile)
    {
        // M2 hotfix (HAND TEST 2: the portable zip runs in "dev" and could not drag, tag or delete): no list, an old agent-import flag on the
        // account, and the write still reaches the server
        var policy = ReadOnlyPolicy.For(profile, _ => null, _ => null);
        var paths = new TestProfilePaths(_root);
        var oauthHttp = new OAuthHttp(_http, TimeProvider.System);
        var flow = new OAuthFlow(new OAuthDiscovery(oauthHttp), _clients, oauthHttp, new RedirectFollowingLauncher(_http), new OAuthFlowOptions { LoopbackPort = 0 }, TimeProvider.System);
        await using var manager = new AccountManager(new JmapClientFactory(_http, TimeProvider.System), new MailAccountFactory(_hooks, TimeProvider.System), _store, flow, oauthHttp, paths, policy, _hooks, TimeProvider.System);
        await _vault.SaveAsync([new StoredAccount(StoredAccount.PasswordKind, OriginA, "ana@example.org") { Password = "pw-a", IsDefault = true, ReadOnly = true }], Ct);

        var restored = await manager.RestoreAsync(Ct);
        await Poll.UntilAsync(() => manager.SessionFor(restored!.Key) is not null, "the restored account's session");
        var client = manager.SessionFor(restored!.Key)!.Client;
        Assert.False(client.IsReadOnly);
        var set = await client.SendAsync(JmapRequest.Of(JmapRequest.Call("Email/set", new JsonObject { ["accountId"] = client.Session.MailAccountId, ["destroy"] = new JsonArray("x") }, "w")), Ct);
        Assert.Null(set.Error);
        Assert.Contains(_a.ApiCalls, c => c.Contains("Email/set", StringComparison.Ordinal));
    }

    [Fact]
    public void The_allow_list_counts_only_from_a_local_folder_and_fails_closed()
    {
        var local = Path.Combine(_root, ".local", "writable-accounts.txt");
        var elsewhere = Path.Combine(_root, "other", "writable-accounts.txt");
        var readOnly = Path.Combine(_root, ".local", "readonly-accounts.txt");
        Func<string, string?> Env(string? file, string? never = null) => n =>
            string.Equals(n, ReadOnlyPolicy.FileVariable, StringComparison.Ordinal) ? file
            : string.Equals(n, ReadOnlyPolicy.ReadOnlyFileVariable, StringComparison.Ordinal) ? never
            : null;
        const string List = "# the test account\r\nana@example.org  # writes go to itself\r\n\r\n";
        string? Read(string p) => string.Equals(p, readOnly, StringComparison.Ordinal) ? "main@example.net\r\n" : List;

        // M2-Q2: without the read-only list nothing is writable; with both, the writable list opens its logins (R2-14: a stored flag too)
        Assert.True(ReadOnlyPolicy.For("auto-1", Env(local), Read).IsReadOnly("ana@example.org", storedFlag: false));
        Assert.False(ReadOnlyPolicy.For("auto-1", Env(local, readOnly), Read).IsReadOnly("ana@example.org", storedFlag: false));
        Assert.True(ReadOnlyPolicy.For("auto-1", Env(local, readOnly), Read).IsReadOnly("ben@example.net", storedFlag: false));
        Assert.False(ReadOnlyPolicy.For("auto-1", Env(local, readOnly), Read).IsReadOnly("ana@example.org", storedFlag: true));
        Assert.Equal(0, ReadOnlyPolicy.For("auto-1", Env(elsewhere, readOnly), Read).WritableCount);
        Assert.Equal(0, ReadOnlyPolicy.For("auto-1", Env(local, readOnly), _ => null).WritableCount);
        Assert.Equal(0, ReadOnlyPolicy.For("smoke", Env(null, readOnly), Read).WritableCount);

        // the real reader: a missing file is read-only
        Assert.Null(ReadOnlyPolicy.ReadAllowList(local));

        // M2 hotfix: the user's own profiles need no list at all; every account is writable, a stored flag too
        Assert.False(ReadOnlyPolicy.For("dev", Env(null), _ => null).IsReadOnly("ben@example.net", storedFlag: true));
        Assert.False(ReadOnlyPolicy.For("main", Env(null), _ => null).IsReadOnly("ana@example.org", storedFlag: false));
    }

    [Fact]
    public async Task A_refused_inactive_account_is_marked_and_the_unread_timer_stops_sending_for_it()
    {
        // Fable review 2 R2-01: the two-minute unread read of a refused account used to send the same password 720 times a day
        Assert.True((await SignInAAsync()).IsOk);
        Assert.True((await SignInBAsync()).IsOk);
        var aKey = AccountKey.Of(OriginA, "ana@example.org");
        var old = _manager.SessionFor(aKey)!;
        await QuietAsync(_a);

        _a.RejectCredentials = true;
        await _manager.RefreshUnreadAsync(Ct);
        Assert.Equal(1, _a.Refused);
        var a = _manager.Statuses.Single(x => string.Equals(x.Account.Key, aKey, StringComparison.Ordinal));
        Assert.True(a.NeedsSignIn);
        Assert.False(a.IsConnected);
        Assert.Equal("ana@example.org needs to sign in again.", a.Error);
        Assert.Null(_manager.SessionFor(aKey));

        // the timer, a switch and the old session's own calls send nothing more
        await _manager.RefreshUnreadAsync(Ct);
        await _manager.RefreshUnreadAsync(Ct);
        Assert.False((await _manager.SwitchToAsync(aKey, Ct)).IsOk);
        Assert.False((await old.Mail.GetMailboxesAsync(Ct)).IsOk);
        Assert.Equal(1, _a.Refused);
        Assert.Equal("ben@example.net", _manager.Active!.User);

        // a new sign-in brings it back
        _a.RejectCredentials = false;
        Assert.True((await SignInAAsync()).IsOk);
        Assert.False(_manager.Statuses.Single(x => string.Equals(x.Account.Key, aKey, StringComparison.Ordinal)).NeedsSignIn);
    }

    [Fact]
    public async Task The_account_on_screen_that_is_refused_asks_for_sign_in_and_a_new_sign_in_reloads_it()
    {
        Assert.True((await SignInAAsync()).IsOk);
        var aKey = AccountKey.Of(OriginA, "ana@example.org");
        var session = _manager.SessionFor(aKey)!;
        var changes = new List<ActiveAccountChangedEventArgs>();
        _manager.ActiveChanged += (_, e) => changes.Add(e);
        await QuietAsync(_a);

        _a.RejectCredentials = true;
        Assert.False((await session.Mail.GetMailboxesAsync(Ct)).IsOk);
        var status = Assert.Single(_manager.Statuses);
        Assert.True(status.IsActive);
        Assert.True(status.NeedsSignIn);
        Assert.Equal(1, _a.Refused);

        _a.RejectCredentials = false;
        Assert.True((await SignInAAsync()).IsOk);
        Assert.NotSame(session, _manager.SessionFor(aKey));

        // same account, new session: the mail screen is told so it reloads
        var renewed = Assert.Single(changes);
        Assert.Equal(aKey, renewed.Current!.Key);
        Assert.Equal(aKey, renewed.Previous!.Key);
    }

    [Fact]
    public async Task Restore_with_a_refused_password_goes_to_sign_in_and_never_sends_it_again()
    {
        await _vault.SaveAsync([new StoredAccount(StoredAccount.PasswordKind, OriginA, "ana@example.org") { Password = "changed-elsewhere", IsDefault = true }], Ct);
        Assert.Null(await _manager.RestoreAsync(Ct));
        var status = Assert.Single(_manager.Statuses);
        Assert.True(status.NeedsSignIn);
        Assert.False(status.IsConnecting);
        Assert.Equal(1, _a.Refused);

        var aKey = AccountKey.Of(OriginA, "ana@example.org");
        Assert.False((await _manager.SwitchToAsync(aKey, Ct)).IsOk);
        Assert.Equal(1, _a.Refused);
    }

    [Fact]
    public async Task Restore_activates_the_first_remembered_account_that_connects()
    {
        // Fable review 2 R2-02: the most recent account's server is down; the next one is shown instead of the sign-in form
        await _vault.SaveAsync(
        [
            new StoredAccount(StoredAccount.PasswordKind, OriginA, "ana@example.org") { Password = "pw-a", IsDefault = true },
            new StoredAccount(StoredAccount.PasswordKind, OriginB, "ben@example.net") { Password = "pw-b" },
        ], Ct);
        _a.Offline = true;
        var restored = await _manager.RestoreAsync(Ct);
        Assert.Equal("ben@example.net", restored!.User);
        var a = _manager.Statuses.Single(x => string.Equals(x.Account.User, "ana@example.org", StringComparison.Ordinal));
        Assert.True(a.IsConnecting);
        Assert.False(a.NeedsSignIn);
    }

    [Fact]
    public async Task Restore_without_network_stays_on_the_account_and_connects_when_the_server_is_back()
    {
        var clock = Fake.VirtualClock();
        var paths = new TestProfilePaths(_root);
        var oauthHttp = new OAuthHttp(_http, clock);
        var flow = new OAuthFlow(new OAuthDiscovery(oauthHttp), _clients, oauthHttp, new RedirectFollowingLauncher(_http), new OAuthFlowOptions { LoopbackPort = 0 }, clock);
        await using var manager = new AccountManager(new JmapClientFactory(_http, clock), new MailAccountFactory(_hooks, clock), _store, flow, oauthHttp, paths, ReadOnlyPolicy.AllReadOnly, _hooks, clock);
        await _vault.SaveAsync(
        [
            new StoredAccount(StoredAccount.PasswordKind, OriginA, "ana@example.org") { Password = "pw-a", IsDefault = true },
            new StoredAccount(StoredAccount.PasswordKind, OriginB, "ben@example.net") { Password = "pw-b" },
        ], Ct);
        _a.Offline = true;
        _b.Offline = true;

        var restored = await manager.RestoreAsync(Ct);
        var aKey = AccountKey.Of(OriginA, "ana@example.org");
        Assert.Equal(aKey, restored!.Key);
        Assert.Null(manager.SessionFor(aKey));
        Assert.True(manager.Statuses[0].IsConnecting);
        var changes = 0;
        manager.ActiveChanged += (_, _) => Interlocked.Increment(ref changes);

        _a.Offline = false;
        _b.Offline = false;
        await Poll.UntilAsync(
            () =>
            {
                clock.Advance(TimeSpan.FromSeconds(1));
                return manager.SessionFor(aKey) is not null && Volatile.Read(ref changes) > 0;
            },
            "the retry to connect the account on screen");
        Assert.Equal(aKey, manager.Active!.Key);
        Assert.Equal(0, _a.Refused);
    }

    // the background unread reads after a sign-in have finished (the fake server saw no new request for a moment)
    private static Task QuietAsync(FakeJmapServer server)
    {
        var seen = -1;
        return Poll.UntilAsync(
            () =>
            {
                var n = server.Requests.Count;
                var quiet = n == seen;
                seen = n;
                return quiet;
            },
            "the server to go quiet");
    }

    // fails the first calls synchronously (a completed task), then connects for real
    private sealed class FailFirstFactory(Barbican.Core.Jmap.IJmapClientFactory inner, int failures) : Barbican.Core.Jmap.IJmapClientFactory
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task<Barbican.Core.Common.Result<Barbican.Core.Jmap.IJmapClient>> ConnectAsync(Barbican.Core.Jmap.ServerEndpoint server, IAccessTokenSource auth, Barbican.Core.Jmap.JmapClientOptions options, CancellationToken ct) =>
            Interlocked.Increment(ref _calls) <= failures
                ? Task.FromResult(Barbican.Core.Common.Result<Barbican.Core.Jmap.IJmapClient>.Fail(new TransportError("down", Retryable: true)))
                : inner.ConnectAsync(server, auth, options, ct);
    }

    // a server that wants a one-time code, with its own manager
    private sealed class OtpRig : IAsyncDisposable
    {
        private readonly FakeJmapServer _server = new(new FakeJmapOptions { Origin = OriginA, Username = "ana@example.org", Password = "pw-a", Otp = "123456" });
        private readonly JmapHttp _http;
        private readonly AccountStore _store;
        private readonly OAuthClientRegistry _clients;
        private readonly string _root = Path.Combine(Path.GetTempPath(), "barbican-otp-" + Guid.NewGuid().ToString("N"));

        public OtpRig()
        {
            _http = new JmapHttp(() => _server);
            _store = new AccountStore(Vault);
            var time = TimeProvider.System;
            var paths = new TestProfilePaths(_root);
            var oauthHttp = new OAuthHttp(_http, time);
            _clients = new OAuthClientRegistry(oauthHttp, paths);
            var hooks = new RecordingHooks();
            var flow = new OAuthFlow(new OAuthDiscovery(oauthHttp), _clients, oauthHttp, new RedirectFollowingLauncher(_http), new OAuthFlowOptions { LoopbackPort = 0 }, time);
            Manager = new AccountManager(new JmapClientFactory(_http, time), new MailAccountFactory(hooks, time), _store, flow, oauthHttp, paths, ReadOnlyPolicy.AllReadOnly, hooks, time);
        }

        public MemoryVault Vault { get; } = new();

        public AccountManager Manager { get; }

        public async ValueTask DisposeAsync()
        {
            await Manager.DisposeAsync();
            _store.Dispose();
            _clients.Dispose();
            _http.Dispose();
            _server.Dispose();
        }
    }
}
