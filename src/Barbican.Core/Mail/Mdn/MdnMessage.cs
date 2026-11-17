// Mirrors: Rust mdn.rs:47 encode_header_word, :54 display_name, :67 base64_body, :80 build, :136 first_address, :161 requested_by, :168 auto_send_ok,
// :185 should_offer, :195 pick_identity; RFC 8098 section 3 (multipart/report), section 2.1 (automatic sending only to the Return-Path), RFC 2047, RFC 2045;
// GHSA-w38p-hpqv-g89c (header injection). Stalwart 0.16.27 has no MDN/send (crates/jmap-proto/src/request/method.rs), so the receipt is built and
// submitted as Email/import + EmailSubmission/set (J-068). Stub from stage F of M2; owner reader.

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Mail.Mdn;

/// <summary>Builds and decides about read receipts.</summary>
public static class MdnMessage
{
    /// <summary>The first address of a Disposition-Notification-To value (commas inside quotes or angle brackets do not split), or null.</summary>
    public static string? FirstAddress(string raw)
    {
        throw new PortPendingException("mdn.rs:136 first_address");
    }

    /// <summary>The address named by the Disposition-Notification-To header, or null.</summary>
    public static string? RequestedBy(JsonArray headers)
    {
        throw new PortPendingException("mdn.rs:161 requested_by");
    }

    /// <summary>Whether a receipt may go out without asking: only when the requested address is the Return-Path (else the From).</summary>
    public static bool AutoSendOk(JsonNode message)
    {
        throw new PortPendingException("mdn.rs:168 auto_send_ok");
    }

    /// <summary>Whether to offer a receipt: requested, not yet sent ($mdnsent), response is not "never", not a draft, folder is not Sent, Drafts, Trash or Junk.</summary>
    public static bool ShouldOffer(JsonNode message, IReadOnlyList<string> roles, string response)
    {
        throw new PortPendingException("mdn.rs:185 should_offer");
    }

    /// <summary>The identity whose address received the message (To, then Cc), else the first identity.</summary>
    public static JsonNode? PickIdentity(IReadOnlyList<JsonNode> identities, JsonNode message)
    {
        throw new PortPendingException("mdn.rs:195 pick_identity");
    }

    /// <summary>The raw multipart/report message with CRLF line ends; header values cannot smuggle a line break.</summary>
    public static string Build(MdnOptions options, DateTimeOffset now, string token)
    {
        throw new PortPendingException("mdn.rs:80 build");
    }

    /// <summary>A display name: an encoded word when non-ASCII, quoted when it has specials.</summary>
    public static string DisplayName(string value)
    {
        throw new PortPendingException("mdn.rs:54 display_name");
    }

    /// <summary>A header value after "Name: ": controls stripped, RFC 2047 encoded when not plain ASCII.</summary>
    public static string EncodeHeaderWord(string value)
    {
        throw new PortPendingException("mdn.rs:47 encode_header_word");
    }

    /// <summary>UTF-8 text as base64 wrapped at 76 characters, CRLF between lines.</summary>
    public static string Base64Body(string text)
    {
        throw new PortPendingException("mdn.rs:67 base64_body");
    }
}
