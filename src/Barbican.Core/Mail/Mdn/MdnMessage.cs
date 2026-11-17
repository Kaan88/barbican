// Mirrors: Rust mdn.rs:25 rfc5322_date, :31 strip_header_controls, :42 header_atom, :47 encode_header_word, :54 display_name, :60 ensure_angles,
// :67 base64_body, :80 build, :136 first_address, :161 requested_by, :168 auto_send_ok, :185 should_offer, :195 pick_identity; RFC 8098 section 3
// (multipart/report; report-type=disposition-notification), section 2.1 (automatic sending only to the Return-Path), RFC 2047, RFC 2045;
// GHSA-w38p-hpqv-g89c (header injection); Bulwark lib/mdn.ts:43-121 (buildMdnMessage). Stalwart 0.16.27 has no MDN/send
// (crates/jmap-proto/src/request/method.rs), so the receipt is built here and submitted as Email/import + EmailSubmission/set (J-068, MdnSender).
// The text is built by hand to the byte (the ported tests pin it); MimeKit parses it back in the tests (M2 plan exit test).

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Mail.Reader;

namespace Barbican.Core.Mail.Mdn;

/// <summary>Builds and decides about read receipts.</summary>
public static class MdnMessage
{
    private static readonly string[] NoReceiptRoles = ["sent", "drafts", "trash", "junk"];
    private static readonly string[] RecipientFields = ["to", "cc"];

    /// <summary>The first address of a Disposition-Notification-To value (commas inside quotes or angle brackets do not split), or null.</summary>
    public static string? FirstAddress(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        var line = raw.Length > 1000 ? raw[..1000] : raw;
        var (quoted, angle, end) = (false, false, line.Length);
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                quoted = !quoted;
            }
            else if (ch == '<' && !quoted)
            {
                angle = true;
            }
            else if (ch == '>' && !quoted)
            {
                angle = false;
            }
            else if (ch == ',' && !quoted && !angle)
            {
                end = i;
                break;
            }
        }

        var first = line[..end];
        var a = first.IndexOf('<', StringComparison.Ordinal);
        var b = first.LastIndexOf('>');
        var addr = (a >= 0 && b > a + 1 ? first[(a + 1)..b] : first).Trim();
        var ok = addr.Count(c => c == '@') == 1 && !addr.StartsWith('@') && !addr.EndsWith('@')
            && !addr.Any(c => char.IsWhiteSpace(c) || c is '<' or '>' or ',' or '"' or ';');
        return ok ? addr : null;
    }

    /// <summary>The address named by the Disposition-Notification-To header, or null.</summary>
    public static string? RequestedBy(JsonArray headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        return ReaderHeaders.Header(headers, "Disposition-Notification-To") is { } v ? FirstAddress(v) : null;
    }

    /// <summary>Whether a receipt may go out without asking: only when the requested address is the Return-Path (else the From).</summary>
    public static bool AutoSendOk(JsonNode message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var headers = message["headers"] as JsonArray ?? [];
        if (RequestedBy(headers) is not { } to)
        {
            return false;
        }

        var origin = (ReaderHeaders.Header(headers, "Return-Path") is { } rp ? FirstAddress(rp) : null) ?? message["from"]?[0].Str("email");
        return origin is not null && string.Equals(origin, to, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Whether to offer a receipt: requested, not yet sent ($mdnsent), response is not "never", not a draft, folder is not Sent, Drafts, Trash or Junk.</summary>
    public static bool ShouldOffer(JsonNode message, IReadOnlyList<string> roles, string response)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(roles);
        return RequestedBy(message["headers"] as JsonArray ?? []) is not null
            && !Flag(message, "$mdnsent")
            && !string.Equals(response, "never", StringComparison.Ordinal)
            && !Flag(message, "$draft")
            && !roles.Any(r => NoReceiptRoles.Contains(r, StringComparer.Ordinal));
    }

    /// <summary>The identity whose address received the message (To, then Cc), else the first identity.</summary>
    public static JsonNode? PickIdentity(IReadOnlyList<JsonNode> identities, JsonNode message)
    {
        ArgumentNullException.ThrowIfNull(identities);
        ArgumentNullException.ThrowIfNull(message);
        var rcpts = RecipientFields
            .SelectMany(k => message[k] as JsonArray ?? [])
            .Select(a => a.Str("email")?.ToLowerInvariant())
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        return identities.FirstOrDefault(i => i.Str("email") is { } e && rcpts.Contains(e.ToLowerInvariant())) ?? (identities.Count > 0 ? identities[0] : null);
    }

    /// <summary>The raw multipart/report message with CRLF line ends; header values cannot smuggle a line break.</summary>
    public static string Build(MdnOptions options, DateTimeOffset now, string token)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(token);
        var to = HeaderAtom(options.To);
        var fromEmail = HeaderAtom(options.FromEmail);
        var origRcpt = HeaderAtom(options.OriginalRecipient);
        var finalRcpt = origRcpt.Length == 0 ? fromEmail : origRcpt;
        var at = fromEmail.IndexOf('@', StringComparison.Ordinal);
        var domainPart = at >= 0 ? fromEmail[(at + 1)..].Split('@')[0] : string.Empty;
        var domain = domainPart.Length > 0 ? domainPart : "localhost";
        var boundary = $"----=_MDN_{token}";
        var origId = EnsureAngles(options.OriginalMessageId);
        var fromHeader = options.FromName.Trim().Length == 0 ? fromEmail : $"{DisplayName(options.FromName)} <{fromEmail}>";
        var subject = EncodeHeaderWord($"Read: {options.OriginalSubject}".Trim());
        var disposition = options.Automatic ? "automatic-action/MDN-sent-automatically; displayed" : "manual-action/MDN-sent-manually; displayed";
        var human = string.Join(
            "\r\n",
            $"This is a return receipt for the message you sent to {finalRcpt}.",
            string.Empty,
            "Note: This receipt only acknowledges that the message was displayed on the",
            "recipient's computer. There is no guarantee that the recipient has read or",
            "understood the message contents.");
        var fields = ReportFields(domain, finalRcpt, origRcpt, origId, disposition);
        var lines = new List<string>
        {
            $"Date: {Rfc5322Date(now)}",
            $"From: {fromHeader}",
            $"To: {to}",
            $"Subject: {subject}",
            $"Message-ID: <mdn.{token}@{domain}>",
        };
        if (origId.Length > 0)
        {
            lines.Add($"In-Reply-To: {origId}");
        }

        lines.AddRange(
        [
            "MIME-Version: 1.0",
            "Content-Type: multipart/report; report-type=disposition-notification;",
            $"\tboundary=\"{boundary}\"",
            string.Empty,
            $"--{boundary}",
            "Content-Type: text/plain; charset=utf-8",
            "Content-Transfer-Encoding: base64",
            string.Empty,
            Base64Body(human),
            string.Empty,
            $"--{boundary}",
            "Content-Type: message/disposition-notification",
            "Content-Transfer-Encoding: 7bit",
            string.Empty,
            fields,
            string.Empty,
            $"--{boundary}--",
            string.Empty,
        ]);
        return string.Join("\r\n", lines);
    }

    // The machine-readable part (RFC 8098 section 3.2).
    private static string ReportFields(string domain, string finalRcpt, string origRcpt, string origId, string disposition)
    {
        var fields = new List<string> { $"Reporting-UA: {domain}; Barbican", $"Final-Recipient: rfc822;{finalRcpt}" };
        if (origRcpt.Length > 0)
        {
            fields.Add($"Original-Recipient: rfc822;{origRcpt}");
        }

        if (origId.Length > 0)
        {
            fields.Add($"Original-Message-ID: {origId}");
        }

        fields.Add($"Disposition: {disposition}");
        return string.Join("\r\n", fields);
    }

    /// <summary>A display name: an encoded word when non-ASCII, quoted when it has specials.</summary>
    public static string DisplayName(string value)
    {
        var e = EncodeHeaderWord(value);
        if (e.StartsWith("=?", StringComparison.Ordinal) || !e.Any(c => "()<>[]:;@\\,.\"".Contains(c, StringComparison.Ordinal)))
        {
            return e;
        }

        return "\"" + e.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    /// <summary>A header value after "Name: ": controls stripped, RFC 2047 encoded when not plain ASCII.</summary>
    public static string EncodeHeaderWord(string value)
    {
        var clean = StripHeaderControls(value);
        return clean.All(c => c is >= ' ' and <= '~') ? clean : $"=?UTF-8?B?{Convert.ToBase64String(Encoding.UTF8.GetBytes(clean))}?=";
    }

    /// <summary>UTF-8 text as base64 wrapped at 76 characters, CRLF between lines.</summary>
    public static string Base64Body(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var b = Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
        return string.Join("\r\n", b.Chunk(76).Select(c => new string(c)));
    }

    /// <summary>RFC 5322 date in UTC, e.g. "Thu, 28 May 2026 14:23:00 +0000".</summary>
    public static string Rfc5322Date(DateTimeOffset t) =>
        t.ToUniversalTime().ToString("ddd, dd MMM yyyy HH:mm:ss '+0000'", CultureInfo.InvariantCulture);

    /// <summary>A token for the Message-ID and boundary: the time in hex, a dot, 16 random hex digits.</summary>
    public static string RandomToken(DateTimeOffset now) =>
        string.Create(CultureInfo.InvariantCulture, $"{now.ToUnixTimeMilliseconds():x}.{Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8))}");

    /// <summary>CR, LF, the other C0 controls and DEL collapse to one space; trimmed.</summary>
    public static string StripHeaderControls(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var sb = new StringBuilder(value.Length);
        var gap = false;
        foreach (var c in value)
        {
            if (c < 0x20 || c == 0x7f)
            {
                if (!gap)
                {
                    sb.Append(' ');
                }

                gap = true;
            }
            else
            {
                sb.Append(c);
                gap = false;
            }
        }

        return sb.ToString().Trim();
    }

    /// <summary>Address-like atoms (mailboxes, message ids) keep no whitespace or controls at all.</summary>
    public static string HeaderAtom(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new string([.. value.Where(c => c > 0x20 && c != 0x7f)]);
    }

    private static string EnsureAngles(string id)
    {
        var t = HeaderAtom(id);
        return t.Length == 0 || t.StartsWith('<') ? t : $"<{t}>";
    }

    private static bool Flag(JsonNode message, string keyword) =>
        message["keywords"]?[keyword] is JsonValue v && v.TryGetValue<bool>(out var b) && b;
}
