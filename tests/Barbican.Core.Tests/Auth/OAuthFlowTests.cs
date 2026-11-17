// OAuth sign-in end to end on FakeJmap with the redirect-following launcher (no browser): Rust oauth.rs sign_in (:60-:200); RFC 6749 §4.1,
// RFC 7636 (PKCE S256), RFC 7591 (dynamic registration), RFC 8252 §7.3 (loopback), RFC 8414 (metadata). spec B-OAUTH-001..018, T-192..T-198.

using Barbican.Core.Auth;
using Barbican.Core.Jmap.Errors;

namespace Barbican.Core.Tests.Auth;

public sealed class OAuthFlowTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Signs_in_with_registration_pkce_and_the_loopback()
    {
        using var rig = new OAuthRig();
        var launcher = new RedirectCapture(rig.Launcher);
        var r = await rig.Flow(launcher).SignInAsync(rig.Base, "user@example.org", "barbican", Ct);

        Assert.True(r.IsOk, r.Error?.Message);
        var t = r.Value!;
        Assert.StartsWith("fake-access-", t.Access, StringComparison.Ordinal);
        Assert.True(rig.Provider.IsValidRefresh(t.Refresh!));
        Assert.Equal("registered-client-1", t.ClientId);
        Assert.Equal(rig.TokenEndpoint, t.TokenEndpoint);
        Assert.NotNull(t.ExpiresAt);
        Assert.Equal(0, rig.Provider.PkceFailures);

        var q = LoopbackListener.ParseQuery(launcher.Authorize!.Query);
        Assert.Equal("code", q["response_type"]);
        Assert.Equal("S256", q["code_challenge_method"]);
        Assert.Equal(43, q["code_challenge"].Length);
        Assert.Equal("user@example.org", q["login_hint"]);
        Assert.Contains("offline_access", q["scope"], StringComparison.Ordinal);
        Assert.Equal("127.0.0.1", launcher.Redirect!.Host);
        Assert.DoesNotContain(t.Access, t.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_forged_callback_with_the_wrong_state_does_not_end_the_sign_in()
    {
        using var rig = new OAuthRig();
        var r = await rig.Flow(new RedirectCapture(rig.Launcher) { ForgeFirst = true }).SignInAsync(rig.Base, "", "barbican", Ct);
        Assert.True(r.IsOk, r.Error?.Message);
        Assert.NotEqual("forged", r.Value!.Access, StringComparer.Ordinal);
    }

    [Fact]
    public async Task The_callback_is_accepted_once()
    {
        using var rig = new OAuthRig();
        var launcher = new RedirectCapture(rig.Launcher);
        Assert.True((await rig.Flow(launcher).SignInAsync(rig.Base, "", "barbican", Ct)).IsOk);

        // replaying the redirect afterwards finds the listener closed
        var replay = await RedirectFollowingLauncher.DeliverAsync(new Uri(launcher.Redirect + "?code=again&state=x"), Ct);
        Assert.False(replay.IsOk);
    }

    [Fact]
    public async Task The_code_is_bound_to_the_verifier()
    {
        using var rig = new OAuthRig();

        // what an attacker holding only the code would try: redeem it without the verifier the app kept
        var verifier = Pkce.NewVerifier();
        var redirect = "http://127.0.0.1:1/callback";
        var authorize = new Uri($"{rig.Server.Base}/oauth/authorize?response_type=code&client_id=barbican&redirect_uri={Uri.EscapeDataString(redirect)}&state=s&code_challenge={Pkce.Challenge(verifier)}&code_challenge_method=S256");
        using var response = await rig.Http.ClientFor(authorize).GetAsync(authorize, Ct);
        var code = LoopbackListener.ParseQuery(response.Headers.Location!.Query)["code"];
        var form = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = redirect, ["client_id"] = "barbican", ["code_verifier"] = Pkce.NewVerifier(),
        };
        var r = TokenResponse.Parse(await rig.OAuth.PostFormAsync(rig.TokenEndpoint, form, Ct), rig.TokenEndpoint, "barbican", null, rig.Time.GetUtcNow());
        Assert.IsType<AuthError>(r.Error);
        Assert.Contains("invalid_grant", r.Error!.Message, StringComparison.Ordinal);
        Assert.Equal(1, rig.Provider.PkceFailures);
    }

    [Fact]
    public async Task The_registered_client_is_reused()
    {
        using var rig = new OAuthRig();
        Assert.True((await rig.Flow().SignInAsync(rig.Base, "", "barbican", Ct)).IsOk);
        Assert.True((await rig.Flow().SignInAsync(rig.Base, "", "barbican", Ct)).IsOk);
        Assert.Equal(1, rig.Provider.Registrations);
        Assert.True(File.Exists(Path.Combine(rig.Paths.ConfigDir, "oauth-clients.json")));
    }

    [Fact]
    public async Task Without_registration_the_client_id_typed_is_used()
    {
        using var rig = new OAuthRig(registration: false);
        var r = await rig.Flow().SignInAsync(rig.Base, "", "  my-client ", Ct);
        Assert.True(r.IsOk, r.Error?.Message);
        Assert.Equal("my-client", r.Value!.ClientId);
        var d = await rig.Flow().SignInAsync(rig.Base, "", "", Ct);
        Assert.Equal(OAuthClientRegistry.DefaultClientId, d.Value!.ClientId);
    }

    [Fact]
    public async Task A_refusal_at_the_provider_is_reported()
    {
        using var rig = new OAuthRig();
        rig.Provider.NextError = "access_denied";
        var r = await rig.Flow().SignInAsync(rig.Base, "", "barbican", Ct);
        Assert.StartsWith("The provider refused the sign-in", r.Error!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_launcher_that_cannot_open_ends_the_sign_in()
    {
        using var rig = new OAuthRig();
        var r = await rig.Flow(new FailingLauncher()).SignInAsync(rig.Base, "", "barbican", Ct);
        Assert.Equal("no browser", r.Error!.Message);
    }

    [Fact]
    public void The_authorize_url_keeps_the_endpoint_query()
    {
        var meta = new OAuthMetadata(new Uri("https://auth.example.org/authorize?tenant=a"), new Uri("https://auth.example.org/token"), null, null, "openid", External: true);
        var u = OAuthFlow.AuthorizeUri(meta, "c", new Uri("http://127.0.0.1:8791/callback"), "ch", "st", null);
        var q = LoopbackListener.ParseQuery(u.Query);
        Assert.Equal("a", q["tenant"]);
        Assert.Equal("c", q["client_id"]);
        Assert.False(q.ContainsKey("login_hint"));
    }

    // M1 hotfix (sign-in): Rust oauth.rs:183 Url::parse_with_params writes application/x-www-form-urlencoded (space as '+'); the app's request
    // is now the Rust app's request byte for byte, so the server's sign-in page (where passkeys live) sees the same query
    [Fact]
    public void The_authorize_query_is_written_as_the_Rust_app_writes_it()
    {
        var meta = new OAuthMetadata(new Uri("https://mail.example.org/login"), new Uri("https://mail.example.org/auth/token"), null, null, "openid offline_access urn:ietf:params:oauth:scope:mail", External: false);
        var u = OAuthFlow.AuthorizeUri(meta, "swc1.Ab-_", new Uri("http://127.0.0.1:8791/callback"), "ch-_", "st", "ana@example.org");
        Assert.Equal(
            "?response_type=code&client_id=swc1.Ab-_&redirect_uri=http%3A%2F%2F127.0.0.1%3A8791%2Fcallback&code_challenge=ch-_&code_challenge_method=S256&state=st"
            + "&scope=openid+offline_access+urn%3Aietf%3Aparams%3Aoauth%3Ascope%3Amail&login_hint=ana%40example.org",
            u.Query);
        Assert.Equal("openid offline_access urn:ietf:params:oauth:scope:mail", LoopbackListener.ParseQuery(u.Query)["scope"]);
        Assert.Equal("a+b%C3%A9%2F", OAuthFlow.FormEncode("a bé/"));
    }

    // M1 hotfix (sign-in): Stalwart behind a TLS proxy answers /api/discover/<account> with issuer "" while announcing RFC 9207 iss; the account
    // hint (now sent, as the Rust app does) takes the sign-in there, and the strict F-04 check stopped it after the user had signed in
    [Fact]
    public async Task A_Stalwart_behind_a_proxy_signs_in_through_its_account_discovery()
    {
        using var rig = new OAuthRig();
        rig.Provider.StalwartLike = true;
        var r = await rig.Flow().SignInAsync(rig.Base, "user@example.org", "barbican", Ct);
        Assert.True(r.IsOk, r.Error?.Message);
        Assert.Equal("user@example.org", Assert.Single(rig.Provider.LoginHints));
    }

    [Fact]
    public async Task The_issuer_of_the_servers_own_metadata_still_guards_the_callback()
    {
        using var rig = new OAuthRig();
        rig.Provider.StalwartLike = true;
        var meta = await new OAuthDiscovery(rig.OAuth).DiscoverAsync(rig.Base, "user@example.org", Ct);
        Assert.Equal(rig.Server.Options.Origin, meta.Value!.Issuer);
        Assert.True(meta.Value.IssParameterSupported);
    }

    private sealed class FailingLauncher : IAuthorizationLauncher
    {
        public Task<Barbican.Core.Common.Result<Barbican.Core.Common.Unit>> OpenAsync(Uri authorizeUri, CancellationToken ct) =>
            Task.FromResult(Barbican.Core.Common.Result<Barbican.Core.Common.Unit>.Fail(new AuthError("no browser", NeedsSignIn: false)));
    }
}
