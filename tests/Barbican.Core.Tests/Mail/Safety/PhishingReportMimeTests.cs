// Bulwark lib/__tests__/phishing-report-arf.test.ts (buildArfParts, rawEncoding, the original byte for byte) and phishing-report.test.ts (the forward).
using System.Text;
using System.Text.RegularExpressions;
using Barbican.Core.Mail.Safety;

namespace Barbican.Core.Tests.Mail.Safety;

public sealed class PhishingReportMimeTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Original = Encoding.ASCII.GetBytes("From: bad@phish.example\r\nSubject: x\r\n\r\nclick here\r\n");

    private static PhishingReportMime.Input Input(string subject = "Konto gesperrt – jetzt prüfen") =>
        new(subject, "alice@example.org", "Alice", "abuse@example.org", new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero), "bad@phish.example", "alice@example.org");

    private static Match Find(string text, string pattern) => Regex.Match(text, pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    [Fact]
    public void An_arf_report_has_the_three_parts_and_the_required_feedback_fields()
    {
        var text = Encoding.UTF8.GetString(PhishingReportMime.Arf(Input() with { AppVersion = "1.2.3" }, Original, Now));

        Assert.StartsWith("Date: Sun, 01 Nov 2026 08:00:00 +0000\r\n", text, StringComparison.Ordinal);
        Assert.Contains("From: Alice <alice@example.org>\r\n", text, StringComparison.Ordinal);
        Assert.Contains("To: abuse@example.org\r\n", text, StringComparison.Ordinal);
        var subject = Find(text, @"Subject: =\?UTF-8\?B\?([^?]+)\?=\r\n");
        Assert.True(subject.Success);
        Assert.Equal("Phishing: Konto gesperrt – jetzt prüfen", Encoding.UTF8.GetString(Convert.FromBase64String(subject.Groups[1].Value)));
        Assert.Contains("Content-Type: multipart/report; report-type=feedback-report;", text, StringComparison.Ordinal);
        var boundary = Find(text, "boundary=\"([^\"]+)\"").Groups[1].Value;
        Assert.Equal(4, text.Split($"--{boundary}\r\n").Length);
        Assert.Contains("Content-Type: message/feedback-report\r\n", text, StringComparison.Ordinal);
        Assert.Contains("\r\nFeedback-Type: fraud\r\n", text, StringComparison.Ordinal);
        Assert.Contains("\r\nUser-Agent: Barbican/1.2.3\r\n", text, StringComparison.Ordinal);
        Assert.Contains("\r\nVersion: 1\r\n", text, StringComparison.Ordinal);
        Assert.Contains("\r\nArrival-Date: Thu, 01 Oct 2026 10:00:00 +0000\r\n", text, StringComparison.Ordinal);
        Assert.Contains("\r\nOriginal-Rcpt-To: alice@example.org\r\n", text, StringComparison.Ordinal);
        Assert.Contains("\r\nReported-Domain: phish.example\r\n", text, StringComparison.Ordinal);
        Assert.Contains("Content-Type: message/rfc822\r\nContent-Disposition: attachment; filename=\"reported-message.eml\"\r\nContent-Transfer-Encoding: 7bit\r\n\r\n", text, StringComparison.Ordinal);
        Assert.EndsWith($"click here\r\n--{boundary}--\r\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_arf_report_leaves_out_the_fields_it_does_not_know()
    {
        var text = Encoding.UTF8.GetString(PhishingReportMime.Arf(new("Hi", "alice@example.org", null, "abuse@example.org", null, null, null), Original, Now));

        Assert.DoesNotContain("Original-Rcpt-To", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Arrival-Date", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Reported-Domain", text, StringComparison.Ordinal);
        Assert.Contains("From: alice@example.org\r\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_forward_attaches_the_original_unchanged_as_a_message_attachment()
    {
        var bytes = PhishingReportMime.Forward(Input("Your parcel") with { FileName = "2026-10-01 10.00.00-Your parcel.eml" }, Original, Now);
        var text = Encoding.UTF8.GetString(bytes);

        Assert.Contains("Content-Type: multipart/mixed;", text, StringComparison.Ordinal);
        Assert.Contains("Subject: Phishing: Your parcel\r\n", text, StringComparison.Ordinal);
        Assert.Contains("To: abuse@example.org\r\n", text, StringComparison.Ordinal);
        Assert.Contains("Content-Type: message/rfc822; name=\"2026-10-01 10.00.00-Your parcel.eml\"\r\nContent-Disposition: attachment; filename=\"2026-10-01 10.00.00-Your parcel.eml\"\r\nContent-Transfer-Encoding: 7bit\r\n\r\n", text, StringComparison.Ordinal);
        var body = Find(text, "Content-Transfer-Encoding: base64\r\n\r\n([A-Za-z0-9+/=\r\n]+)\r\n\r\n--").Groups[1].Value;
        Assert.Contains("Reported as phishing by Alice <alice@example.org> with", Encoding.UTF8.GetString(Convert.FromBase64String(body.Replace("\r\n", string.Empty, StringComparison.Ordinal))), StringComparison.Ordinal);
        var boundary = Find(text, "boundary=\"([^\"]+)\"").Groups[1].Value;
        Assert.EndsWith(Encoding.ASCII.GetString(Original) + $"--{boundary}--\r\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_forward_names_a_non_ascii_attachment_the_way_RFC_2231_does_and_cannot_break_out_of_the_parameter()
    {
        var text = Encoding.UTF8.GetString(PhishingReportMime.Forward(Input() with { FileName = "Grüße \"x\"\r\nBcc: injected.eml" }, Original, Now));

        Assert.Contains("filename=\"Gr__e x Bcc: injected.eml\"; filename*=UTF-8''Gr%C3%BC%C3%9Fe%20x%20Bcc%3A%20injected.eml", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\r\nBcc:", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_original_goes_in_byte_for_byte_even_when_it_is_not_text()
    {
        byte[] binary = [.. Original[..^2], 0x00, 0xff, 0x0a];

        var bytes = PhishingReportMime.Arf(Input(), binary, Now);

        var at = bytes.AsSpan().IndexOf(binary);
        Assert.True(at > 0);
        Assert.Equal("\r\n", Encoding.ASCII.GetString(bytes, at + binary.Length, 2));
        Assert.Contains("Content-Transfer-Encoding: binary\r\n\r\n", Encoding.Latin1.GetString(bytes), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("a\r\nb\r\n", "7bit")]
    [InlineData("Grüße\r\n", "8bit")]
    [InlineData("a\nb", "binary")]
    [InlineData("a\rb", "binary")]
    [InlineData("a\0b", "binary")]
    public void The_raw_encoding_is_7bit_8bit_or_binary_never_base64(string text, string expected) =>
        Assert.Equal(expected, PhishingReportMime.RawEncoding(Encoding.UTF8.GetBytes(text)));

    [Fact]
    public void A_line_over_998_octets_is_binary() =>
        Assert.Equal("binary", PhishingReportMime.RawEncoding(Encoding.ASCII.GetBytes(new string('x', 999) + "\r\n")));
}
