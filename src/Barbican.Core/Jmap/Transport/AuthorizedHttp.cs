// Mirrors: Rust jmap.rs:242 (API), :783 (download), :814 (upload), :1119 (event source): on 401 renew the token once and send again;
// spec B-JMAP-017, B-OAUTH-015 (the token source adopts a token rotated elsewhere; a 401 never deletes the stored one); RFC 6750 §3.1
// (invalid_token). The token source decides whether a renewal exists: a static header answers "sign in again". Owner jmap.

using System.Net;
using System.Net.Http.Headers;
using Barbican.Core.Auth;
using Barbican.Core.Common;

namespace Barbican.Core.Jmap.Transport;

/// <summary>Sends one HTTP request with the current Authorization header, renewing it and retrying once after a 401.</summary>
internal static class AuthorizedHttp
{
    public static async Task<Result<HttpResponseMessage>> SendAsync(
        HttpClient http, IAccessTokenSource auth, Func<HttpRequestMessage> make, HttpCompletionOption completion, CancellationToken ct)
    {
        var header = await auth.GetAsync(ct).ConfigureAwait(false);
        if (!header.IsOk)
        {
            return Result<HttpResponseMessage>.Fail(header.Error!);
        }

        var current = header.Value!;
        for (var renewed = false; ; renewed = true)
        {
            var response = await SendOnceAsync(http, make, current, completion, ct).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.Unauthorized || renewed)
            {
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

    private static async Task<HttpResponseMessage> SendOnceAsync(
        HttpClient http, Func<HttpRequestMessage> make, AuthenticationHeaderValue header, HttpCompletionOption completion, CancellationToken ct)
    {
        using var request = make();
        request.Headers.Authorization = header;
        return await http.SendAsync(request, completion, ct).ConfigureAwait(false);
    }
}
