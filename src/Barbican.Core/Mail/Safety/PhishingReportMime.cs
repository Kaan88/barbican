// Mirrors: Bulwark lib/phishing-report-arf.ts (buildArfParts :45, rawEncoding :111, buildArfReportBlob :132; RFC 5965 multipart/report, Feedback-Type fraud,
// section 7.3) and lib/phishing-report.ts:128-150 (the forward format: the original attached as message/rfc822, one message per report, RFC 5965 section
// 2(e)). Bulwark attaches the forward by blobId through Email/set; a raw submission has no such path, so the original's bytes (downloaded from its own
// account) go into the MIME unchanged, which reads the same to the report desk. Owner reader.

using System.Globalization;
using System.Text;
using Barbican.Core.Mail.Mdn;

namespace Barbican.Core.Mail.Safety;

/// <summary>The raw MIME of a phishing report around the reported message's own bytes.</summary>
public static class PhishingReportMime
{
    private static readonly byte[] Crlf = "\r\n"u8.ToArray();

    /// <summary>What a report says about the reported message and its reporter.</summary>
    public sealed record Input(string Subject, string ReporterEmail, string? ReporterName, string To, DateTimeOffset? ReceivedAt, string? FromAddress, string? DeliveredTo)
    {
        public string AppVersion { get; init; } = "unknown";

        /// <summary>The name of the attachment of a forward ("2026-11-21 10.00.00-Subject.eml").</summary>
        public string FileName { get; init; } = "message.eml";
    }

    /// <summary>The forward format: a short text and the original attached unchanged.</summary>
    public static byte[] Forward(Input input, byte[] original, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(original);
        var from = MdnMessage.HeaderAtom(input.ReporterEmail);
        var boundary = $"----=_REPORT_{MdnMessage.RandomToken(now)}";
        var fileName = AttachmentName(input.FileName);
        var head = string.Join("\r\n",
        [
            .. Common(input, now, "mixed", "Message-ID: <report." + MdnMessage.RandomToken(now) + "@" + Domain(from) + ">", boundary),
            $"--{boundary}",
            "Content-Type: text/plain; charset=utf-8",
            "Content-Transfer-Encoding: base64",
            string.Empty,
            MdnMessage.Base64Body(PhishingReportRules.Text(input.ReporterName, from)),
            string.Empty,
            $"--{boundary}",
            $"Content-Type: message/rfc822; {fileName.Parameter("name")}",
            $"Content-Disposition: attachment; {fileName.Parameter("filename")}",
            $"Content-Transfer-Encoding: {RawEncoding(original)}",
            string.Empty,
            string.Empty,
        ]);
        return Assemble(head, original, boundary);
    }

    /// <summary>The ARF format (RFC 5965): text for people, the machine-readable feedback report and the original unchanged.</summary>
    public static byte[] Arf(Input input, byte[] original, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(original);
        var from = MdnMessage.HeaderAtom(input.ReporterEmail);
        var boundary = $"----=_ARF_{MdnMessage.RandomToken(now)}";
        var human = string.Join("\r\n", $"This is a phishing report (RFC 5965) sent by {from} with Barbican.", string.Empty, "The reported message is attached unchanged.");
        var fields = new List<string> { "Feedback-Type: fraud", $"User-Agent: Barbican/{MdnMessage.StripHeaderControls(input.AppVersion)}", "Version: 1" };
        if (input.ReceivedAt is { } arrival)
        {
            fields.Add($"Arrival-Date: {MdnMessage.Rfc5322Date(arrival)}");
        }

        if (!string.IsNullOrWhiteSpace(input.DeliveredTo))
        {
            fields.Add($"Original-Rcpt-To: {MdnMessage.HeaderAtom(input.DeliveredTo)}");
        }

        if (ReportedDomain(input.FromAddress) is { } domain)
        {
            fields.Add($"Reported-Domain: {domain}");
        }

        var head = string.Join("\r\n",
        [
            .. Common(input, now, "report; report-type=feedback-report", "Message-ID: <arf." + MdnMessage.RandomToken(now) + "@" + Domain(from) + ">", boundary),
            $"--{boundary}",
            "Content-Type: text/plain; charset=utf-8",
            "Content-Transfer-Encoding: base64",
            string.Empty,
            MdnMessage.Base64Body(human),
            string.Empty,
            $"--{boundary}",
            "Content-Type: message/feedback-report",
            "Content-Transfer-Encoding: 7bit",
            string.Empty,
            string.Join("\r\n", fields),
            string.Empty,
            $"--{boundary}",
            "Content-Type: message/rfc822",
            "Content-Disposition: attachment; filename=\"reported-message.eml\"",
            $"Content-Transfer-Encoding: {RawEncoding(original)}",
            string.Empty,
            string.Empty,
        ]);
        return Assemble(head, original, boundary);
    }

    /// <summary>7bit or 8bit lines of at most 998 octets ending in CRLF (RFC 2045 sections 2.7, 2.8); anything else (a NUL, a bare CR or LF, a longer line) is binary.</summary>
    public static string RawEncoding(ReadOnlySpan<byte> bytes)
    {
        var eightBit = false;
        var line = 0;
        for (var i = 0; i < bytes.Length; i++)
        {
            var b = bytes[i];
            if (b == 0x00)
            {
                return "binary";
            }

            if (b == 0x0d)
            {
                if (i + 1 >= bytes.Length || bytes[i + 1] != 0x0a)
                {
                    return "binary";
                }

                i++;
                line = 0;
                continue;
            }

            if (b == 0x0a)
            {
                return "binary";
            }

            eightBit |= b > 0x7f;
            if (++line > 998)
            {
                return "binary";
            }
        }

        return eightBit ? "8bit" : "7bit";
    }

    private static IEnumerable<string> Common(Input input, DateTimeOffset now, string subtype, string messageId, string boundary)
    {
        var from = MdnMessage.HeaderAtom(input.ReporterEmail);
        var name = string.IsNullOrWhiteSpace(input.ReporterName) ? string.Empty : MdnMessage.DisplayName(input.ReporterName.Trim());
        yield return $"Date: {MdnMessage.Rfc5322Date(now)}";
        yield return $"From: {(name.Length > 0 ? $"{name} <{from}>" : from)}";
        yield return $"To: {MdnMessage.HeaderAtom(input.To)}";
        yield return $"Subject: {MdnMessage.EncodeHeaderWord(PhishingReportRules.Subject(input.Subject))}";
        yield return messageId;
        yield return "MIME-Version: 1.0";
        yield return $"Content-Type: multipart/{subtype};";
        yield return $"\tboundary=\"{boundary}\"";
        yield return string.Empty;
    }

    // the original goes in byte for byte; the closing boundary needs a line break before it
    private static byte[] Assemble(string head, byte[] original, string boundary)
    {
        var endsWithBreak = original.Length >= 2 && original[^2] == 0x0d && original[^1] == 0x0a;
        using var output = new MemoryStream();
        output.Write(Encoding.UTF8.GetBytes(head));
        output.Write(original);
        if (!endsWithBreak)
        {
            output.Write(Crlf);
        }

        output.Write(Encoding.UTF8.GetBytes($"--{boundary}--\r\n"));
        return output.ToArray();
    }

    private static string Domain(string address) => address.Contains('@', StringComparison.Ordinal) ? address[(address.LastIndexOf('@') + 1)..] : "localhost";

    private static string? ReportedDomain(string? address)
    {
        if (address is null || !address.Contains('@', StringComparison.Ordinal))
        {
            return null;
        }

        var domain = address[(address.LastIndexOf('@') + 1)..];
        return domain.Length > 0 && domain.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-') ? domain.ToLowerInvariant() : null;
    }

    // a name as a quoted ASCII parameter, with an RFC 2231 form when it is not plain ASCII
    private static (string Plain, string? Extended) AttachmentName(string name)
    {
        var clean = MdnMessage.StripHeaderControls(name).Replace("\"", string.Empty, StringComparison.Ordinal).Replace("\\", string.Empty, StringComparison.Ordinal);
        var ascii = new string([.. clean.Select(c => c is >= ' ' and <= '~' ? c : '_')]);
        if (string.Equals(ascii, clean, StringComparison.Ordinal))
        {
            return (ascii, null);
        }

        var encoded = new StringBuilder();
        foreach (var b in Encoding.UTF8.GetBytes(clean))
        {
            if (char.IsAsciiLetterOrDigit((char)b) || b is (byte)'.' or (byte)'-' or (byte)'_')
            {
                encoded.Append((char)b);
            }
            else
            {
                encoded.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return (ascii, encoded.ToString());
    }

    private static string Parameter(this (string Plain, string? Extended) name, string key) =>
        name.Extended is null ? $"{key}=\"{name.Plain}\"" : $"{key}=\"{name.Plain}\"; {key}*=UTF-8''{name.Extended}";
}
