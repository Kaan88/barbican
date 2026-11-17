// Mirrors: Rust reader.rs:74 valid_unsub_url, :83 parse_unsubscribe, :117 unsubscribe_mailto, valid_email; RFC 2369, RFC 8058;
// Bulwark components/email/unsubscribe-banner.tsx, lib/validation.ts:71. Stub from stage F of M2; owner reader.

using Barbican.Core.Common;

namespace Barbican.Core.Mail.Reader;

/// <summary>Parses the List-Unsubscribe and List-Unsubscribe-Post headers.</summary>
public static class UnsubscribeParser
{
    /// <summary>
    /// The usable targets of a List-Unsubscribe value, or null when there is none (javascript: links, no angle brackets, several or invalid
    /// mailto recipients are refused). <paramref name="postHeader"/> is the List-Unsubscribe-Post value.
    /// </summary>
    public static UnsubscribeLinks? Parse(string listUnsubscribe, string? postHeader)
    {
        throw new PortPendingException("reader.rs:83 parse_unsubscribe");
    }

    /// <summary>The recipient, subject and body of a mailto: link, or null when the address is not valid.</summary>
    public static UnsubscribeMailto? ParseMailto(string mailto)
    {
        throw new PortPendingException("reader.rs:117 unsubscribe_mailto");
    }

    /// <summary>A plain address check: one @, a dotted domain, no spaces.</summary>
    public static bool ValidEmail(string address)
    {
        throw new PortPendingException("reader.rs valid_email");
    }
}
