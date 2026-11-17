// Mirrors: Rust auth.rs (parse :34, topmost :55, domain_of :60, aligned :66, aligned_dkim_pass :72, aligned_pass :79, failure :90);
// RFC 8601; DMARC relaxed alignment (RFC 7489 §3.1); Bulwark lib/email-headers.ts. Stub from F2; owner mailcore.

using Barbican.Core.Common;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Mail.AuthResults;

/// <summary>Reads the receiving server's Authentication-Results; only a pass that aligns with the From domain says anything.</summary>
public static class AuthenticationResults
{
    /// <summary>Parses one Authentication-Results value (the authserv-id is skipped, comments stripped).</summary>
    public static IReadOnlyList<AuthResult> Parse(string value) => throw new PortPendingException("AuthenticationResults.Parse: mailcore stream");

    /// <summary>The topmost Authentication-Results of a header list (the one our server wrote).</summary>
    public static IReadOnlyList<AuthResult> Topmost(IReadOnlyList<EmailHeader> headers) => throw new PortPendingException("AuthenticationResults.Topmost: mailcore stream");

    /// <summary>The domain part of an address, lower-case, without a trailing dot; null if it has no dot.</summary>
    public static string? DomainOf(string address) => throw new PortPendingException("AuthenticationResults.DomainOf: mailcore stream");

    /// <summary>DMARC relaxed alignment: the same domain or a parent/child of it.</summary>
    public static bool Aligned(string fromDomain, string? candidate) => throw new PortPendingException("AuthenticationResults.Aligned: mailcore stream");

    /// <summary>A DKIM pass whose signing domain aligns with the From domain.</summary>
    public static bool AlignedDkimPass(IReadOnlyList<AuthResult> results, string fromDomain) =>
        throw new PortPendingException("AuthenticationResults.AlignedDkimPass: mailcore stream");

    /// <summary>Whether the checks tie the message to its From address: dmarc=pass, or an aligned SPF (MAIL FROM, never HELO) or DKIM pass.</summary>
    public static bool AlignedPass(IReadOnlyList<AuthResult> results, string from) =>
        throw new PortPendingException("AuthenticationResults.AlignedPass: mailcore stream");

    /// <summary>A failure that leaves the From domain unauthenticated.</summary>
    public static bool Failure(IReadOnlyList<AuthResult> results, bool verified) => throw new PortPendingException("AuthenticationResults.Failure: mailcore stream");
}
