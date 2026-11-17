// Mirrors: Rust jmap.rs:242 (API), :783 (download), :814 (upload), :1119 (event source): on 401 renew the token once and send again;
// spec B-JMAP-017, B-OAUTH-015 (the token source adopts a token rotated elsewhere; a 401 never deletes the stored one); RFC 6750 §3.1
// (invalid_token). The token source decides whether a renewal exists: a static header answers "sign in again". Fable review 1 F-02: the
// header goes only to an origin the caller allows (the typed server and the https hosts its session names), checked before the token
// source is even asked. Fable review 2 R2-01: a header that was just renewed and is refused too closes the source's AuthLatch, so nothing
// more is sent with it (a refused source closes the latch itself). Owner jmap.

using System.Net;
using System.Net.Http.Headers;
using Barbican.Core.Auth;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;

namespace Barbican.Core.Jmap.Transport;

/// <summary>Sends one HTTP request with the current Authorization header, renewing it and retrying once after a 401.</summary>
internal static class AuthorizedHttp
{
    /// <param name="http">The client of <paramref name="target"/>'s origin.</param>
    /// <param name="auth">The token source.</param>
    /// <param name="target">The URL every request <paramref name="make"/> builds goes to.</param>
    /// <param name="allowed">Whether credentials may go to <paramref name="target"/>; false fails without sending anything.</param>
    /// <param name="make">Builds the request (once per attempt).</param>
    /// <param name="completion">When the send completes.</param>
    /// <param name="ct">Cancels the send.</param>
    public static async Task<Result<HttpResponseMessage>> SendAsync(
        HttpClient http, IAccessTokenSource auth, Uri target, Func<Uri, bool> allowed, Func<HttpRequestMessage> make, HttpCompletionOption completion, CancellationToken ct)
    {
        if (!allowed(target))
        {
            return Result<HttpResponseMessage>.Fail(Refused(target));
        }

        var header = await auth.GetAsync(ct).ConfigureAwait(false);
        if (!header.IsOk)
        {
            return Result<HttpResponseMessage>.Fail(header.Error!);
        }

        var current = header.Value!;
        for (var renewed = false; ; renewed = true)
        {
            var response = await SendOnceAsync(http, target, make, current, completion, ct).ConfigureAwait(false);
            if (response is null)
            {
                return Result<HttpResponseMessage>.Fail(Refused(target));
            }

            if (response.StatusCode != HttpStatusCode.Unauthorized)
            {
                return Result<HttpResponseMessage>.Ok(response);
            }

            if (renewed)
            {
                // the renewed header is refused as well: no further request may carry it (R2-01); the caller still reads the 401
                (auth as AuthLatch)?.Close(new AuthError("Session expired; sign in again.", NeedsSignIn: true));
                return Result<HttpResponseMessage>.Ok(response);
            }

            response.Dispose();
            var next = await auth.RenewAfterRejectAsync(current, ct).ConfigureAwait(false);
            if (!next.IsOk)
            {
                return Result<HttpResponseMessage>.Fail(next.Error!);
            }

            current = next.Value!;
        }
    }

    /// <summary>The refusal of a request whose target is not a server of this login.</summary>
    public static TransportError Refused(Uri target) =>
        new($"Barbican did not send your sign-in to {target.Host}: it is not this account's server.", Retryable: false);

    // null: the request built does not go where the caller checked (the header is never attached)
    private static async Task<HttpResponseMessage?> SendOnceAsync(
        HttpClient http, Uri target, Func<HttpRequestMessage> make, AuthenticationHeaderValue header, HttpCompletionOption completion, CancellationToken ct)
    {
        using var request = make();
        if (request.RequestUri != target)
        {
            return null;
        }

        request.Headers.Authorization = header;
        return await http.SendAsync(request, completion, ct).ConfigureAwait(false);
    }
}
