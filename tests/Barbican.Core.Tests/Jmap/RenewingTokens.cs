// A token source that renews by asking the fake server's OAuth endpoint shape: each renewal hands out the next token the server
// will accept (the fake adds "fake-access-N" on a refresh grant). Owner jmap.

using System.Net.Http.Headers;
using Barbican.Core.Auth;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;

namespace Barbican.Core.Tests.Jmap;

internal sealed class RenewingTokens(Func<Task<string?>> renew) : IAccessTokenSource
{
    private string _token = "fake-access-1";

    public int Renewals { get; private set; }

    public ValueTask<Result<AuthenticationHeaderValue>> GetAsync(CancellationToken ct) =>
        ValueTask.FromResult(Result<AuthenticationHeaderValue>.Ok(new AuthenticationHeaderValue("Bearer", _token)));

    public async ValueTask<Result<AuthenticationHeaderValue>> RenewAfterRejectAsync(AuthenticationHeaderValue rejected, CancellationToken ct)
    {
        Renewals++;
        if (await renew() is not { } next)
        {
            return Result<AuthenticationHeaderValue>.Fail(new AuthError("Session expired; sign in again.", NeedsSignIn: true));
        }

        _token = next;
        return Result<AuthenticationHeaderValue>.Ok(new AuthenticationHeaderValue("Bearer", next));
    }
}
