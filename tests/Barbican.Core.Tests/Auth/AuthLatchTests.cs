// Fable review 2 R2-01: a refused sign-in must not be sent again. Stalwart counts every failed login and bans the client address after 100 a
// day by default (crates/common/src/auth/authentication.rs:39-76, crates/registry/src/schema/structs_impl.rs:35589-35592), so each call site
// may meet a 401 once and then nothing more is sent: API, download, push, the session re-read and the OAuth refresh grant. RFC 6750 §3.1,
// RFC 6749 §5.2. A network error never closes the latch.

using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Barbican.Core.Auth;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Transport;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Service;
using Barbican.Core.Sync;
using Barbican.Core.Tests.Jmap;
using Barbican.FakeJmap;

namespace Barbican.Core.Tests.Auth;

public sealed class AuthLatchTests
{
    private const string Key = "https://jmap.example.org\nuser@example.org";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static JmapRequest Echo() => JmapRequest.Of(JmapRequest.Call("Core/echo", new JsonObject { ["n"] = 1 }, "e"));

    private sealed class CountingSource(Result<AuthenticationHeaderValue> answer) : IAccessTokenSource
    {
        public int Calls { get; private set; }

        public ValueTask<Result<AuthenticationHeaderValue>> GetAsync(CancellationToken ct)
        {
            Calls++;
            return ValueTask.FromResult(answer);
        }

        public ValueTask<Result<AuthenticationHeaderValue>> RenewAfterRejectAsync(AuthenticationHeaderValue rejected, CancellationToken ct)
        {
            Calls++;
            return ValueTask.FromResult(answer);
        }
    }

    [Fact]
    public async Task A_refused_password_meets_the_server_once_and_then_nothing_is_sent()
    {
        var f = await Fake.StartAsync();
        var blob = f.Server.Store.Email(f.Server.Store.EmailIds()[0])!["blobId"]!.GetValue<string>();
        var closed = 0;
        f.Client.Auth.Closed += (_, _) => closed++;

        // the password was changed on the server while the client runs (or the one-time code's hour is over)
        f.Server.RejectCredentials = true;
        var first = await f.Client.SendAsync(Echo(), Ct);
        Assert.True(Assert.IsType<AuthError>(first.Error).NeedsSignIn);
        Assert.Equal(1, f.Server.Refused);
        Assert.True(f.Client.Auth.IsClosed);
        Assert.Equal(1, closed);

        var before = f.Server.Requests.Count;
        Assert.True(Assert.IsType<AuthError>((await f.Client.SendAsync(Echo(), Ct)).Error).NeedsSignIn);
        Assert.IsType<AuthError>((await f.Client.DownloadAsync("b", blob, "m.eml", "message/rfc822", Ct)).Error);
        Assert.IsType<AuthError>((await f.Client.OpenEventSourceAsync(["Email"], 30, Ct)).Error);
        _ = await f.Client.RefreshSessionAsync(Ct);

        Assert.Equal(before, f.Server.Requests.Count);
        Assert.Equal(1, f.Server.Refused);
        Assert.Equal(1, closed);
    }

    [Fact]
    public async Task A_renewed_token_that_is_refused_too_closes_the_latch()
    {
        var server = new FakeJmapServer();
        var tokens = new RenewingTokens(() => Task.FromResult<string?>(server.IssueToken()));
        var http = new JmapHttp(() => server);
        var session = await Sessions.FetchAsync(http, tokens, new Uri(server.Base + "/.well-known/jmap"), TimeSpan.FromSeconds(30), TimeProvider.System, Ct);
        using var client = new JmapClient(http, tokens, session.Value!, new JmapClientOptions(), TimeProvider.System, JmapTrace.Off);

        // the account was disabled: even a fresh token is refused
        server.RejectCredentials = true;
        Assert.True(Assert.IsType<AuthError>((await client.SendAsync(Echo(), Ct)).Error).NeedsSignIn);
        Assert.Equal(2, server.Refused);
        Assert.Equal(1, tokens.Renewals);
        Assert.True(client.Auth.IsClosed);

        Assert.True(Assert.IsType<AuthError>((await client.SendAsync(Echo(), Ct)).Error).NeedsSignIn);
        Assert.Equal(2, server.Refused);
        Assert.Equal(1, tokens.Renewals);
    }

    [Fact]
    public async Task A_network_error_does_not_close_the_latch()
    {
        var f = await Fake.StartAsync();
        f.Server.Offline = true;
        var down = await f.Client.SendAsync(Echo(), Ct);
        Assert.IsNotType<AuthError>(down.Error);
        f.Server.Offline = false;
        Assert.False(f.Client.Auth.IsClosed);
        Assert.True((await f.Client.SendAsync(Echo(), Ct)).IsOk);
    }

    [Fact]
    public async Task A_source_that_says_sign_in_again_is_never_asked_again()
    {
        var inner = new CountingSource(Result<AuthenticationHeaderValue>.Fail(new AuthError("Session expired; sign in again.", NeedsSignIn: true)));
        var latch = AuthLatch.Over(inner);
        Assert.Same(latch, AuthLatch.Over(latch));
        Assert.False((await latch.GetAsync(Ct)).IsOk);
        Assert.False((await latch.GetAsync(Ct)).IsOk);
        Assert.False((await latch.RenewAfterRejectAsync(new AuthenticationHeaderValue("Bearer", "x"), Ct)).IsOk);
        Assert.Equal(1, inner.Calls);

        var flaky = new CountingSource(Result<AuthenticationHeaderValue>.Fail(new TransportError("token endpoint down", Retryable: true)));
        var open = new AuthLatch(flaky);
        _ = await open.GetAsync(Ct);
        _ = await open.GetAsync(Ct);
        Assert.False(open.IsClosed);
        Assert.Equal(2, flaky.Calls);
    }

    [Fact]
    public async Task A_refused_refresh_token_is_posted_once()
    {
        using var rig = new OAuthRig();
        var store = new MemoryRefreshStore();

        // the refresh token was revoked at the provider (it never knew this one)
        using var source = new OAuthTokenSource(rig.OAuth, store, rig.Paths, rig.Time, Key, new OAuthTokens(FakeJmapServer.AccessToken, "revoked-refresh", null, rig.TokenEndpoint, "barbican"));
        var client = (await new JmapClientFactory(rig.Http, rig.Time).ConnectAsync(ServerEndpoint.Parse(rig.Server.Base)!, source, new JmapClientOptions(), Ct)).Value!;
        var mail = new MailService(client, rig.Time);
        rig.Server.ExpireToken(FakeJmapServer.AccessToken);

        Assert.True(Assert.IsType<AuthError>((await mail.GetMailboxesAsync(Ct)).Error).NeedsSignIn);
        Assert.Equal(1, rig.Provider.RefreshGrants);
        Assert.Equal(1, rig.Server.Refused);

        for (var i = 0; i < 3; i++)
        {
            Assert.False((await mail.GetMailboxesAsync(Ct)).IsOk);
        }

        Assert.Equal(1, rig.Provider.RefreshGrants);
        Assert.Equal(1, rig.Server.Refused);
    }

    [Fact]
    public async Task Push_and_sync_end_after_a_refusal_instead_of_retrying()
    {
        var clock = Fake.VirtualClock();
        var f = await Fake.StartAsync(time: clock);
        var s = new MailService(f.Client, clock);
        using var sync = new MailSync(s, new RecordingHooks(), clock, new PushBackoff(_ => 0));
        f.Server.RejectCredentials = true;

        var run = sync.RunAsync(Ct);
        var problem = Assert.IsType<SyncProblemEvent>(await sync.Events.ReadAsync(Ct).AsTask().WaitAsync(TimeSpan.FromSeconds(10), Ct));
        Assert.True(problem.NeedsSignIn);

        // the engine ends by itself: no re-open with backoff, no polling
        await run.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        Assert.True(sync.SignInNeeded);
        for (var i = 0; i < 5; i++)
        {
            clock.Advance(TimeSpan.FromMinutes(10));
            sync.Schedule(MailSync.PushTypes);
            clock.Advance(MailSync.Coalesce);
        }

        await sync.DrainAsync();
        Assert.Equal(1, f.Server.Refused);
    }
}
