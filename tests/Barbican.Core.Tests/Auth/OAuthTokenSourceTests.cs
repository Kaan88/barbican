// The access-token side of OAuth: Rust oauth.rs refresh (:300-:380: adopt the stored token, a rotation elsewhere is retried once, nothing is
// deleted) and jmap.rs's 401 → renew → retry once, here through the real transport (JmapClientFactory → AuthorizedHttp) on FakeJmap.
// RFC 6749 §6 (refresh), §5.2 (invalid_grant). spec B-OAUTH-010..018, J-020 (token side).

using Barbican.Core.Auth;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Transport;
using Barbican.Core.Mail.Service;

namespace Barbican.Core.Tests.Auth;

public sealed class OAuthTokenSourceTests
{
    private const string Key = "https://jmap.example.org\nuser@example.org";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static OAuthTokens Tokens(OAuthRig rig, string access, string? refresh, DateTimeOffset? expires = null) =>
        new(access, refresh, expires, rig.TokenEndpoint, "barbican");

    [Fact]
    public async Task A_401_renews_once_and_the_request_goes_through()
    {
        using var rig = new OAuthRig();
        var store = new MemoryRefreshStore();
        var refresh = rig.Provider.AddRefreshToken();
        store.Set(Key, refresh);
        using var source = new OAuthTokenSource(rig.OAuth, store, rig.Paths, rig.Time, Key, Tokens(rig, Barbican.FakeJmap.FakeJmapServer.AccessToken, refresh));
        var refreshed = 0;
        source.Refreshed += (_, _) => refreshed++;

        var client = (await new JmapClientFactory(rig.Http, rig.Time).ConnectAsync(ServerEndpoint.Parse(rig.Server.Base)!, source, new JmapClientOptions(), Ct)).Value!;
        var mail = new MailService(client, rig.Time);
        Assert.True((await mail.GetMailboxesAsync(Ct)).IsOk);

        rig.Server.ExpireToken(Barbican.FakeJmap.FakeJmapServer.AccessToken);
        var after = await mail.GetMailboxesAsync(Ct);

        Assert.True(after.IsOk, after.Error?.Message);
        Assert.Equal(1, source.Renewals);
        Assert.Equal(1, refreshed);
        Assert.Equal(1, rig.Provider.RefreshGrants);

        // the rotated refresh token was written back; the used one is dead at the provider
        Assert.Equal(source.Tokens.Refresh, store.Get(Key));
        Assert.False(rig.Provider.IsValidRefresh(refresh));
    }

    [Fact]
    public async Task An_expired_access_token_is_renewed_before_use()
    {
        using var rig = new OAuthRig();
        var store = new MemoryRefreshStore();
        var refresh = rig.Provider.AddRefreshToken();
        using var source = new OAuthTokenSource(rig.OAuth, store, rig.Paths, rig.Time, Key, Tokens(rig, "old", refresh, rig.Time.GetUtcNow().AddMinutes(-1)));
        var h = await source.GetAsync(Ct);
        Assert.True(h.IsOk, h.Error?.Message);
        Assert.NotEqual("old", h.Value!.Parameter, StringComparer.Ordinal);
        Assert.Equal(1, source.Renewals);
    }

    [Fact]
    public async Task Concurrent_rejections_of_the_same_token_renew_once()
    {
        using var rig = new OAuthRig();
        var store = new MemoryRefreshStore();
        var refresh = rig.Provider.AddRefreshToken();
        using var source = new OAuthTokenSource(rig.OAuth, store, rig.Paths, rig.Time, Key, Tokens(rig, "a", refresh));
        var rejected = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "a");
        var all = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => source.RenewAfterRejectAsync(rejected, Ct).AsTask()));
        Assert.All(all, r => Assert.True(r.IsOk));
        Assert.Single(all.Select(r => r.Value!.Parameter).Distinct(StringComparer.Ordinal));
        Assert.Equal(1, rig.Provider.RefreshGrants);
    }

    [Fact]
    public async Task A_token_rotated_by_another_process_is_adopted()
    {
        using var rig = new OAuthRig();
        var store = new MemoryRefreshStore();
        var mine = rig.Provider.AddRefreshToken();
        var theirs = rig.Provider.AddRefreshToken();

        // the other process used "mine" (dead now) and stored "theirs"
        store.Set(Key, theirs);
        using var source = new OAuthTokenSource(rig.OAuth, store, rig.Paths, rig.Time, Key, Tokens(rig, "a", mine));
        var r = await source.RenewAfterRejectAsync(new("Bearer", "a"), Ct);
        Assert.True(r.IsOk, r.Error?.Message);
        Assert.False(rig.Provider.IsValidRefresh(theirs));
        Assert.Equal(source.Tokens.Refresh, store.Get(Key));
    }

    [Fact]
    public async Task A_rejection_while_another_process_rotates_is_retried_once_with_the_stored_token()
    {
        using var rig = new OAuthRig();
        var store = new RotatingStore(rig);
        var dead = "fake-refresh-dead";
        using var source = new OAuthTokenSource(rig.OAuth, store, rig.Paths, rig.Time, Key, Tokens(rig, "a", dead));
        var r = await source.RenewAfterRejectAsync(new("Bearer", "a"), Ct);
        Assert.True(r.IsOk, r.Error?.Message);
        Assert.Equal(2, rig.Provider.RefreshGrants);
    }

    [Fact]
    public async Task A_dead_refresh_token_asks_for_sign_in_and_deletes_nothing()
    {
        using var rig = new OAuthRig();
        var store = new MemoryRefreshStore();
        store.Set(Key, "fake-refresh-dead");
        using var source = new OAuthTokenSource(rig.OAuth, store, rig.Paths, rig.Time, Key, Tokens(rig, "a", "fake-refresh-dead"));
        var r = await source.RenewAfterRejectAsync(new("Bearer", "a"), Ct);
        var e = Assert.IsType<AuthError>(r.Error);
        Assert.True(e.NeedsSignIn);
        Assert.StartsWith(OAuthTokenSource.NeedsSignInPrefix, e.Message, StringComparison.Ordinal);
        Assert.Equal("fake-refresh-dead", store.Get(Key));
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task A_refused_stored_token_falls_back_to_the_one_in_memory_once()
    {
        // R2-06: the vault holds a token the provider no longer takes (another instance wrote it, then lost it); this instance still holds a live one
        using var rig = new OAuthRig();
        var store = new MemoryRefreshStore();
        var mine = rig.Provider.AddRefreshToken();
        store.Set(Key, "fake-refresh-dead");
        using var source = new OAuthTokenSource(rig.OAuth, store, rig.Paths, rig.Time, Key, Tokens(rig, "a", mine));
        var r = await source.RenewAfterRejectAsync(new("Bearer", "a"), Ct);
        Assert.True(r.IsOk, r.Error?.Message);
        Assert.Equal(2, rig.Provider.RefreshGrants); // the stored one, then the one in memory
        Assert.Equal(source.Tokens.Refresh, store.Get(Key));
    }

    [Fact]
    public async Task A_server_outage_is_transient_and_keeps_the_tokens()
    {
        using var rig = new OAuthRig();
        var store = new MemoryRefreshStore();
        var refresh = rig.Provider.AddRefreshToken();
        using var source = new OAuthTokenSource(rig.OAuth, store, rig.Paths, rig.Time, Key, Tokens(rig, "a", refresh));
        rig.Provider.TokenEndpointDown = true;
        var r = await source.RenewAfterRejectAsync(new("Bearer", "a"), Ct);
        Assert.IsType<TransportError>(r.Error);
        Assert.Equal(refresh, source.Tokens.Refresh);
        Assert.True(rig.Provider.IsValidRefresh(refresh));
    }

    [Fact]
    public async Task Without_a_refresh_token_it_asks_for_sign_in()
    {
        using var rig = new OAuthRig();
        using var source = new OAuthTokenSource(rig.OAuth, new MemoryRefreshStore(), rig.Paths, rig.Time, Key, Tokens(rig, "a", null));
        var r = await source.RenewAfterRejectAsync(new("Bearer", "a"), Ct);
        Assert.True(Assert.IsType<AuthError>(r.Error).NeedsSignIn);
        Assert.Equal(0, rig.Provider.RefreshGrants);
    }

    [Fact]
    public async Task The_cross_process_lock_is_exclusive_and_released()
    {
        using var rig = new OAuthRig();
        var path = Path.Combine(rig.Paths.ConfigDir, "oauth-refresh.lock");
        using (var first = await RefreshLock.AcquireAsync(rig.Paths, rig.Time, Ct))
        {
            Assert.True(first!.IsHeld);

            // Fable F-04: the lock is the open handle, which the OS drops when the holder dies; nobody else can open it meanwhile
            Assert.Throws<IOException>(() => new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None).Dispose());
        }

        // the file stays (a stale file is no lock), and the next holder takes it at once
        Assert.True(File.Exists(path));
        using var again = await RefreshLock.AcquireAsync(rig.Paths, rig.Time, Ct);
        Assert.True(again!.IsHeld);
    }

    // the stored token changes between the first attempt and the re-read (another process finished its rotation meanwhile)
    private sealed class RotatingStore(OAuthRig rig) : IRefreshTokenStore
    {
        private string? _value;
        private int _reads;

        public Task<string?> ReadAsync(string accountKey, CancellationToken ct)
        {
            _reads++;
            if (_reads == 2)
            {
                _value = rig.Provider.AddRefreshToken();
            }

            return Task.FromResult(_value);
        }

        public Task WriteAsync(string accountKey, string refresh, CancellationToken ct)
        {
            _value = refresh;
            return Task.CompletedTask;
        }
    }
}
