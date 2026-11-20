// RFC 5322 sender verification without changing the signed MIME bytes.
using MimeKit;

namespace Barbican.Core.Compose;

internal static class RawMessageHeaders
{
    private const int MaxHeaderBytes = 64 * 1024;

    internal static bool MatchFrom(byte[] bytes, string identity)
    {
        var length = HeaderLength(bytes);
        if (length == 0) return false;
        try
        {
            using var stream = new MemoryStream(bytes, 0, length, writable: false);
            var options = new ParserOptions { AddressParserComplianceMode = RfcComplianceMode.Strict };
            var headers = new MimeParser(options, stream, false).ParseHeaders();
            var from = headers.Where(header => header.Id == HeaderId.From).ToArray();
            return from.Length == 1 && InternetAddressList.TryParse(options, from[0].Value, out var addresses) &&
                addresses.Count == 1 && addresses[0] is MailboxAddress mailbox &&
                string.Equals(mailbox.Address, identity, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is FormatException or IOException or ArgumentException) { return false; }
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
