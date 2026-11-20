// RFC 5322 sender verification without changing the signed MIME bytes.
using Barbican.Core.Compose.Recipients;
using Barbican.Core.OrganizationPolicy;
using MimeKit;

namespace Barbican.Core.Compose;

internal static class RawMessageHeaders
{
    private const int MaxHeaderBytes = 64 * 1024;

    internal static bool TryRead(byte[] bytes, string identity, bool protectBlindRecipients, bool deriveRecipients, out string[] recipients,
        string? expectedSubject = null, IReadOnlyList<string>? visibleRecipients = null)
    {
        recipients = [];
        if (protectBlindRecipients && !RawClassification.HasValidHeaderBlock(bytes)) return false;
        var length = HeaderLength(bytes);
        if (length == 0) return false;
        try
        {
            using var stream = new MemoryStream(bytes, 0, length, writable: false);
            var options = new ParserOptions { AddressParserComplianceMode = RfcComplianceMode.Strict };
            var headers = new MimeParser(options, stream, false).ParseHeaders();
            var from = headers.Where(header => header.Id == HeaderId.From).ToArray();
            if (from.Length != 1 || !InternetAddressList.TryParse(options, from[0].Value, out var addresses) ||
                addresses.Count != 1 || addresses[0] is not MailboxAddress mailbox ||
                !string.Equals(mailbox.Address, identity, StringComparison.OrdinalIgnoreCase)) return false;

            // Blind recipients belong only in the SMTP envelope so the signed source can be sent as supplied.
            if (protectBlindRecipients && headers.Any(header => header.Id is HeaderId.Bcc or HeaderId.ResentBcc &&
                !string.IsNullOrWhiteSpace(header.Value))) return false;
            if (expectedSubject is not null && !MatchesSubject(headers, expectedSubject)) return false;
            if (visibleRecipients is not null && (!TryReadRecipients(headers, options, out var visible, allowEmpty: true) ||
                !visible.All(address => visibleRecipients.Contains(address, StringComparer.Ordinal)))) return false;
            return !deriveRecipients || TryReadRecipients(headers, options, out recipients);
        }
        catch (Exception error) when (error is FormatException or IOException or ArgumentException) { return false; }
    }

    private static bool MatchesSubject(HeaderList headers, string expected)
    {
        var subjects = headers.Where(header => header.Id == HeaderId.Subject).ToArray();
        return subjects.Length == 0 ? expected.Length == 0 : subjects.Length == 1 &&
            string.Equals(subjects[0].Value, expected, StringComparison.Ordinal);
    }

    private static bool TryReadRecipients(HeaderList headers, ParserOptions options, out string[] recipients, bool allowEmpty = false)
    {
        recipients = [];
        var result = new List<string>();
        foreach (var header in headers.Where(header => header.Id is HeaderId.To or HeaderId.Cc))
        {
            if (!InternetAddressList.TryParse(options, header.Value, out var addresses)) return false;
            foreach (var mailbox in addresses.Mailboxes)
            {
                if (result.Count == 200 || !RecipientText.IsValidEmail(mailbox.Address)) return false;
                result.Add(RecipientText.ToAsciiEmail(mailbox.Address));
            }
        }
        recipients = [.. result.Distinct(StringComparer.Ordinal)];
        return allowEmpty || recipients.Length > 0;
    }

    private static int HeaderLength(byte[] bytes)
    {
        for (var i = 0; i < Math.Min(bytes.Length - 1, MaxHeaderBytes); i++)
        {
            if (bytes[i] == 0) return 0;
            if (bytes[i] == 10 && bytes[i + 1] == 10) return i + 2;
            if (i + 3 < bytes.Length && i + 4 <= MaxHeaderBytes && bytes[i] == 13 && bytes[i + 1] == 10 && bytes[i + 2] == 13 && bytes[i + 3] == 10) return i + 4;
        }
        return 0;
    }
}
