// Mirrors: Rust reader.rs:54 Unsub (the parsed List-Unsubscribe header); RFC 2369 section 3.2, RFC 8058 section 3. Stub from stage F of M2; owner reader.

namespace Barbican.Core.Mail.Reader;

/// <summary>The usable targets of a List-Unsubscribe header.</summary>
/// <param name="Http">The https or http link, or null.</param>
/// <param name="Mailto">The mailto: link, or null.</param>
/// <param name="OneClick">True when the sender offers RFC 8058 one-click for an https link (exact token List-Unsubscribe=One-Click).</param>
public sealed record UnsubscribeLinks(string? Http, string? Mailto, bool OneClick)
{
    /// <summary>Whether the https link is used before the mailto one.</summary>
    public bool PrefersHttp => Http is not null && (OneClick || Mailto is null);
}
