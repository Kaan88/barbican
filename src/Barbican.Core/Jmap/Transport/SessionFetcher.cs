// Mirrors: Rust jmap.rs:73 fetch_session (GET /.well-known/jmap, redirects followed, Accept: application/json), :145 (401: wrong
// credentials), :216 refresh_session; Bulwark lib/jmap/client.ts:2156 connect (401, 402 "TOTP/MFA code required");
// RFC 8620 §2.2 (autodiscovery; the well-known URL may redirect); RFC 9110 §15.4 (redirects) and the Fetch standard (Authorization is
// dropped on a cross-origin redirect, so a redirect to another host never receives the credentials). Owner jmap.

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Barbican.Core.Auth;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Json;

namespace Barbican.Core.Jmap.Transport;

/// <summary>Fetches and parses the session resource, following redirects itself.</summary>
public static class SessionFetcher
{
    private const int MaxRedirects = 5;

    /// <summary>The session behind <paramref name="url"/>, or why there is none.</summary>
    public static async Task<Result<JmapSession>> FetchAsync(
        IJmapHttp http, IAccessTokenSource auth, Uri url, TimeSpan timeout, TimeProvider time, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(auth);
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(time);
        using var deadline = new CancellationTokenSource(timeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
        try
        {
            return await FollowAsync(http, auth, url, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Result<JmapSession>.Fail(new TransportError("The server did not answer in time.", Retryable: true));
        }
        catch (HttpRequestException e)
        {
            return Result<JmapSession>.Fail(new TransportError($"Network error: {e.Message}", Retryable: true));
        }
    }

    private static async Task<Result<JmapSession>> FollowAsync(IJmapHttp http, IAccessTokenSource auth, Uri start, CancellationToken ct)
    {
        var url = start;
        var origin = start.GetLeftPart(UriPartial.Authority);
        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            var sameOrigin = string.Equals(url.GetLeftPart(UriPartial.Authority), origin, StringComparison.OrdinalIgnoreCase);
            HttpRequestMessage Make()
            {
                var m = new HttpRequestMessage(HttpMethod.Get, url);
                m.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                return m;
            }

            HttpResponseMessage response;
            if (sameOrigin)
            {
                var sent = await AuthorizedSendAsync(http, auth, url, Make, ct).ConfigureAwait(false);
                if (!sent.IsOk)
                {
                    // the token source has no header, or no renewal after a 401
                    return Result<JmapSession>.Fail(sent.Error!);
                }

                response = sent.Value!;
            }
            else
            {
                using var bare = Make();
                response = await http.ClientFor(url).SendAsync(bare, ct).ConfigureAwait(false);
            }

            using var owned = response;
            if (IsRedirect(response.StatusCode) && response.Headers.Location is { } location)
            {
                var next = location.IsAbsoluteUri ? location : new Uri(url, location);
                if (string.Equals(url.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) && !string.Equals(next.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
                {
                    return Result<JmapSession>.Fail(new TransportError("The server redirected the session to an insecure address.", Retryable: false));
                }

                url = next;
                continue;
            }

            return await ReadAsync(response, url, ct).ConfigureAwait(false);
        }

        return Result<JmapSession>.Fail(new TransportError("Too many redirects while looking up the server.", Retryable: false));
    }

    private static async Task<Result<HttpResponseMessage>> AuthorizedSendAsync(
        IJmapHttp http, IAccessTokenSource auth, Uri url, Func<HttpRequestMessage> make, CancellationToken ct) =>
        await AuthorizedHttp.SendAsync(http.ClientFor(url), auth, make, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);

    private static async Task<Result<JmapSession>> ReadAsync(HttpResponseMessage response, Uri url, CancellationToken ct)
    {
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        switch (response.StatusCode)
        {
            case HttpStatusCode.Unauthorized:
                return Result<JmapSession>.Fail(new AuthError("Wrong username, password or one-time code.", NeedsSignIn: true));
            case HttpStatusCode.PaymentRequired when NeedsOtp(text):
                return Result<JmapSession>.Fail(new OtpRequiredError("Enter the one-time code from your authenticator app."));
            case var s when !response.IsSuccessStatusCode:
                return Result<JmapSession>.Fail(new TransportError($"Server returned {(int)s}", Retryable: (int)s >= 500));
            default:
                return SessionParser.Parse(text, url);
        }
    }

    private static bool NeedsOtp(string body)
    {
        try
        {
            var title = (JsonNode.Parse(body).Str("title") ?? "").ToLowerInvariant();
            return title.Contains("totp", StringComparison.Ordinal) || title.Contains("mfa", StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsRedirect(HttpStatusCode s) => s is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
        or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;
}
