// Mirrors: Rust reader.rs:169 one_click_post, :179 post_form; RFC 8058 section 3.1 (POST List-Unsubscribe=One-Click); Bulwark components/email/unsubscribe-banner.tsx.
// Stub from stage F of M2; owner reader.

using Barbican.Core.Common;

namespace Barbican.Core.Net;

/// <summary>The RFC 8058 one-click unsubscribe POST.</summary>
public static class OneClickUnsubscribe
{
    /// <summary>
    /// Posts <c>List-Unsubscribe=One-Click</c>: https only, only to a public address, no redirects, no cookies or credentials. A refusal names the
    /// reason ("needs an https link", "points into a private network").
    /// </summary>
    public static Task<Result<Unit>> PostAsync(IGuardedHttp http, string url, CancellationToken ct)
    {
        throw new PortPendingException("reader.rs:169 one_click_post");
    }

    /// <summary>The POST itself without the https check (the checks live in the client); a 3xx answer is the failure "redirected".</summary>
    internal static Task<Result<Unit>> PostFormAsync(IGuardedHttp http, string url, CancellationToken ct)
    {
        throw new PortPendingException("reader.rs:179 post_form");
    }
}
