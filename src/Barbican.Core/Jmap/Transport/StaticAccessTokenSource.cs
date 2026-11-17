// Mirrors: Rust jmap.rs:125 connect (Basic user:password, a one-time code appended Stalwart-style as `password$code`), :136 the
// Bearer header; spec B-OAUTH-015 (a 401 never deletes what is stored). The real, renewing source is the accounts stream's; this one
// serves tests, the live suite and a plain password login. Owner jmap.

using System.Net.Http.Headers;
using System.Text;
using Barbican.Core.Auth;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;

namespace Barbican.Core.Jmap.Transport;

/// <summary>A fixed Authorization header that cannot be renewed: a rejection means the user must sign in again.</summary>
public sealed class StaticAccessTokenSource(AuthenticationHeaderValue header) : IAccessTokenSource
{
    /// <summary>Basic auth; a TOTP code is appended to the password as <c>password$code</c> (Stalwart).</summary>
    public static StaticAccessTokenSource Basic(string user, string password, string? totp = null)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(password);
        var pass = string.IsNullOrWhiteSpace(totp) ? password : $"{password}${totp.Trim()}";
        return new(new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{pass}"))));
    }

    /// <summary>A bearer token.</summary>
    public static StaticAccessTokenSource Bearer(string token) => new(new AuthenticationHeaderValue("Bearer", token));

    /// <inheritdoc/>
    public ValueTask<Result<AuthenticationHeaderValue>> GetAsync(CancellationToken ct) => ValueTask.FromResult(Result<AuthenticationHeaderValue>.Ok(header));

    /// <inheritdoc/>
    public ValueTask<Result<AuthenticationHeaderValue>> RenewAfterRejectAsync(AuthenticationHeaderValue rejected, CancellationToken ct) =>
        ValueTask.FromResult(Result<AuthenticationHeaderValue>.Fail(new AuthError("Session expired; sign in again.", NeedsSignIn: true)));
}
