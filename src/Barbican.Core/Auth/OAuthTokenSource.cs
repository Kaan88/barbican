// Mirrors: Rust oauth.rs:328 refresh_with (grant_type=refresh_token, the old refresh token kept when none comes back), :338 refresh (under
// the cross-process lock: adopt the stored token first; on a rejection re-read the store and retry once with a token rotated elsewhere;
// never delete), :270 NEEDS_SIGN_IN ("Session expired"); jmap.rs:242 (on 401 renew once and send again; the transport does the retry,
// AuthorizedHttp). RFC 6749 §6 (refreshing), §5.2 (errors); Stalwart crates/common/src/auth/oauth/token.rs (refresh tokens rotate).
// Bulwark lib/oauth/tokens.ts and slot-renewal.ts renew ahead of expiry the same way. spec B-OAUTH-012..B-OAUTH-016, J-020 (token side).

using System.Net.Http.Headers;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Storage;

namespace Barbican.Core.Auth;

/// <summary>The renewing bearer token of one OAuth account.</summary>
public sealed class OAuthTokenSource : IAccessTokenSource, IDisposable
{
    /// <summary>The message prefix of an account that must sign in again (Rust oauth.rs:270); the account is kept.</summary>
    public const string NeedsSignInPrefix = "Session expired";

    private readonly OAuthHttp _http;
    private readonly IRefreshTokenStore _store;
    private readonly IProfilePaths _paths;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private OAuthTokens _tokens;

    public OAuthTokenSource(OAuthHttp http, IRefreshTokenStore store, IProfilePaths paths, TimeProvider time, string accountKey, OAuthTokens tokens)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(accountKey);
        ArgumentNullException.ThrowIfNull(tokens);
        _http = http;
        _store = store;
        _paths = paths;
        _time = time;
        AccountKey = accountKey;
        _tokens = tokens;
    }

    /// <summary>Raised after the access token was renewed (the onTokenRefresh hook).</summary>
    public event EventHandler? Refreshed;

    /// <summary>The account these tokens belong to.</summary>
    public string AccountKey { get; private set; }

    /// <summary>The current tokens.</summary>
    public OAuthTokens Tokens => Volatile.Read(ref _tokens);

    /// <summary>Successful renewals so far.</summary>
    public int Renewals { get; private set; }

    /// <inheritdoc/>
    public async ValueTask<Result<AuthenticationHeaderValue>> GetAsync(CancellationToken ct)
    {
        var t = Tokens;
        if (t.Access.Length > 0 && (t.ExpiresAt is not { } at || _time.GetUtcNow() < at))
        {
            return Result<AuthenticationHeaderValue>.Ok(Bearer(t.Access));
        }

        return await RenewAsync(t.Access, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public ValueTask<Result<AuthenticationHeaderValue>> RenewAfterRejectAsync(AuthenticationHeaderValue rejected, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rejected);
        return RenewAsync(rejected.Parameter ?? "", ct);
    }

    /// <summary>Names the account once it is known (an OAuth sign-in learns the login from the JMAP session).</summary>
    public void BindAccount(string accountKey)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        AccountKey = accountKey;
    }

    public void Dispose() => _gate.Dispose();

    private static AuthenticationHeaderValue Bearer(string token) => new("Bearer", token);

    private static Result<AuthenticationHeaderValue> SignInAgain(string? detail) =>
        Result<AuthenticationHeaderValue>.Fail(new AuthError($"{NeedsSignInPrefix}; sign in again.{(detail is null ? "" : $" ({detail})")}", NeedsSignIn: true));

    // one renewal at a time; a caller whose rejected token was already replaced gets the new one without another refresh
    private async ValueTask<Result<AuthenticationHeaderValue>> RenewAsync(string staleAccess, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var t = Tokens;
            if (t.Access.Length > 0 && !string.Equals(t.Access, staleAccess, StringComparison.Ordinal) && (t.ExpiresAt is not { } at || _time.GetUtcNow() < at))
            {
                return Result<AuthenticationHeaderValue>.Ok(Bearer(t.Access));
            }

            if (t.Refresh is not { Length: > 0 } mine)
            {
                return SignInAgain(null);
            }

            using var crossProcess = await RefreshLock.AcquireAsync(_paths, _time, ct).ConfigureAwait(false);
            return await RefreshAsync(t, mine, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    // Rust oauth.rs:338: adopt the stored token first; a rejection with a newer stored token is retried once; never delete anything
    private async Task<Result<AuthenticationHeaderValue>> RefreshAsync(OAuthTokens t, string mine, CancellationToken ct)
    {
        var used = await _store.ReadAsync(AccountKey, ct).ConfigureAwait(false) is { Length: > 0 } stored ? stored : mine;
        var retried = false;
        while (true)
        {
            var r = await _http.PostFormAsync(t.TokenEndpoint, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = used,
                ["client_id"] = t.ClientId,
            }, ct).ConfigureAwait(false);
            var parsed = TokenResponse.Parse(r, t.TokenEndpoint, t.ClientId, used, _time.GetUtcNow());
            if (parsed.IsOk)
            {
                var next = parsed.Value!;
                Volatile.Write(ref _tokens, next);
                Renewals++;
                if (next.Refresh is { } rotated && !string.Equals(rotated, used, StringComparison.Ordinal))
                {
                    await _store.WriteAsync(AccountKey, rotated, ct).ConfigureAwait(false);
                }

                Refreshed?.Invoke(this, EventArgs.Empty);
                return Result<AuthenticationHeaderValue>.Ok(Bearer(next.Access));
            }

            var error = parsed.Error!;
            var rejected = error is AuthError && OAuthRefresh.IsRejection(error.Message);
            var now = await _store.ReadAsync(AccountKey, ct).ConfigureAwait(false);
            switch (OAuthRefresh.Decide(used, now, rejected))
            {
                case RetryWithStored retry when !retried:
                    retried = true;
                    used = retry.RefreshToken;
                    continue;
                case RetryWithStored or NeedsSignIn:
                    return SignInAgain(error.Message);
                default:
                    return Result<AuthenticationHeaderValue>.Fail(error is TransportError ? error : new TransportError(error.Message, Retryable: true));
            }
        }
    }
}
