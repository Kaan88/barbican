// Mirrors: Rust reader.rs:169 one_click_post, :179 post_form; RFC 8058 section 3.1 (POST List-Unsubscribe=One-Click as
// application/x-www-form-urlencoded, no cookies or credentials; the sender answers the POST itself, so a redirect is a failure);
// Bulwark components/email/unsubscribe-banner.tsx. The address checks live in GuardedHttp (resolve once, refuse any private answer, pin).

using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;

namespace Barbican.Core.Net;

/// <summary>The RFC 8058 one-click unsubscribe POST.</summary>
public static class OneClickUnsubscribe
{
    /// <summary>The form body of the POST.</summary>
    public const string FormBody = "List-Unsubscribe=One-Click";

    /// <summary>
    /// Posts <c>List-Unsubscribe=One-Click</c>: https only, only to a public address, no redirects, no cookies or credentials. A refusal names the
    /// reason ("needs an https link", "points into a private network").
    /// </summary>
    public static async Task<Result<Unit>> PostAsync(IGuardedHttp http, string url, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(url);
        if (!url.Trim().StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return Result<Unit>.Fail(new ValidationError("One-click unsubscribe needs an https link."));
        }

        var r = await PostFormAsync(http, url, ct).ConfigureAwait(false);
        return !r.IsOk && r.Error is ValidationError { Text: GuardedHttp.PrivateNetwork }
            ? Result<Unit>.Fail(new ValidationError("The unsubscribe link points into a private network."))
            : r;
    }

    /// <summary>The POST itself without the https check (the checks live in the client); a 3xx answer is the failure "redirected".</summary>
    internal static async Task<Result<Unit>> PostFormAsync(IGuardedHttp http, string url, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            return Result<Unit>.Fail(new ValidationError("The link is not a web address."));
        }

        var sent = await http.SendAsync(new GuardedRequest(HttpMethod.Post, uri, FormBody), ct).ConfigureAwait(false);
        if (!sent.IsOk)
        {
            return Result<Unit>.Fail(sent.Error!);
        }

        var status = sent.Value!.StatusCode;
        if (status is >= 300 and < 400)
        {
            return Result<Unit>.Fail(new ValidationError("The sender's server redirected the request; open the link in a browser instead."));
        }

        return status is >= 200 and < 300
            ? Result<Unit>.Ok(default)
            : Result<Unit>.Fail(new ValidationError($"The sender's server answered {status}."));
    }
}
