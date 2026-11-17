// Mirrors: Fable review 2 R2-01 (a refused sign-in was sent again and again: the push re-open, the two-minute unread timer and every sync
// sent the same rejected Basic header or posted the same refused refresh token). Stalwart counts every failed login and bans the client
// address after 100 a day by default, with no end (crates/common/src/auth/authentication.rs:39-76 is_auth_fail2banned;
// crates/registry/src/schema/structs_impl.rs:35589-35592; crates/common/src/network/security.rs:206-215). A Basic header with a one-time
// code (password$code) is re-checked after the access-token lifetime (crates/http/src/auth/authenticate.rs:32, :86, :94-101), so such a
// session ends with a 401 after about an hour. RFC 6750 §3.1 (invalid_token), RFC 6749 §5.2 (invalid_grant). Rust jmap.rs:242 and
// oauth.rs:270 had no latch (inherited). Owner accounts.

using System.Net.Http.Headers;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;

namespace Barbican.Core.Auth;

/// <summary>
/// Wraps a token source and closes for good once the server refused it and nothing can renew it (a 401 the source cannot answer, or a
/// refused refresh token): every later call fails here with <see cref="AuthError"/> (NeedsSignIn) and nothing is sent. A new sign-in
/// creates a new source, so a new latch.
/// </summary>
public sealed class AuthLatch : IAccessTokenSource
{
    private AuthError? _closed;

    public AuthLatch(IAccessTokenSource inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        Inner = inner;
    }

    /// <summary>Raised once, on the thread that closed the latch.</summary>
    public event EventHandler? Closed;

    /// <summary>The wrapped source.</summary>
    public IAccessTokenSource Inner { get; }

    /// <summary>Why the latch closed, or null while it is open.</summary>
    public AuthError? Reason => Volatile.Read(ref _closed);

    /// <summary>True once the server refused the credentials for good.</summary>
    public bool IsClosed => Reason is not null;

    /// <summary><paramref name="source"/> itself when it already is a latch, else a new latch over it.</summary>
    public static AuthLatch Over(IAccessTokenSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source as AuthLatch ?? new AuthLatch(source);
    }

    /// <inheritdoc/>
    public async ValueTask<Result<AuthenticationHeaderValue>> GetAsync(CancellationToken ct)
    {
        if (Reason is { } closed)
        {
            return Result<AuthenticationHeaderValue>.Fail(closed);
        }

        return Watch(await Inner.GetAsync(ct).ConfigureAwait(false));
    }

    /// <inheritdoc/>
    public async ValueTask<Result<AuthenticationHeaderValue>> RenewAfterRejectAsync(AuthenticationHeaderValue rejected, CancellationToken ct)
    {
        if (Reason is { } closed)
        {
            return Result<AuthenticationHeaderValue>.Fail(closed);
        }

        return Watch(await Inner.RenewAfterRejectAsync(rejected, ct).ConfigureAwait(false));
    }

    /// <summary>Closes the latch (the transport calls this when a freshly renewed header is refused too).</summary>
    public void Close(AuthError reason)
    {
        ArgumentNullException.ThrowIfNull(reason);
        if (Interlocked.CompareExchange(ref _closed, reason, null) is null)
        {
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }

    private Result<AuthenticationHeaderValue> Watch(Result<AuthenticationHeaderValue> r)
    {
        // only a refusal closes it: a network error or a token endpoint that is down is retried as before
        if (!r.IsOk && r.Error is AuthError { NeedsSignIn: true } refused)
        {
            Close(refused);
        }

        return r;
    }
}
