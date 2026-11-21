// Bulwark lib/__tests__/phishing-report.test.ts ("which messages can be reported", reportSubject, reporterIdentity).
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Safety;

namespace Barbican.Core.Tests.Mail.Safety;

public sealed class PhishingReportRulesTests
{
    [Fact]
    public void Not_your_own_mail_drafts_or_a_message_reported_already()
    {
        Assert.True(PhishingReportRules.IsReportable(Keywords.None, "inbox", []));
        Assert.True(PhishingReportRules.IsReportable(Keywords.None, "junk", []));
        foreach (var role in new[] { "sent", "drafts", "scheduled" })
        {
            Assert.False(PhishingReportRules.IsReportable(Keywords.None, role, []), role);
            Assert.False(PhishingReportRules.IsReportable(Keywords.None, "all", [role]), role);
        }

        Assert.False(PhishingReportRules.IsReportable(Keywords.Of("$draft"), "inbox", []));
        Assert.False(PhishingReportRules.IsReportable(Keywords.Of("$phishing"), "junk", []));
        Assert.True(PhishingReportRules.IsReportable(Keywords.None, null, [null, "inbox"]));
    }

    [Fact]
    public void The_subject_says_phishing_and_a_line_break_cannot_start_a_header()
    {
        Assert.Equal("Phishing", PhishingReportRules.Subject(""));
        Assert.Equal("Phishing", PhishingReportRules.Subject(null));
        Assert.Equal("Phishing: Your parcel", PhishingReportRules.Subject("Your parcel"));
        Assert.Equal("Phishing: a Bcc: x@evil.example", PhishingReportRules.Subject("a\r\nBcc: x@evil.example"));
    }

    [Fact]
    public void The_text_names_the_reporter()
    {
        Assert.Contains("Reported as phishing by Alice <alice@example.org> with", PhishingReportRules.Text("Alice", "alice@example.org"), StringComparison.Ordinal);
        Assert.Contains("Reported as phishing by alice@example.org with", PhishingReportRules.Text(" ", "alice@example.org"), StringComparison.Ordinal);
        Assert.EndsWith("The reported message is attached unchanged.\r\n", PhishingReportRules.Text(null, "alice@example.org"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_report_goes_out_from_the_logins_own_address_never_a_wildcard()
    {
        Identity[] identities = [new("w", "Wild", "*@example.org"), new("o", "Other", "other@example.org"), new("me", "Alice", "Alice@Example.org")];

        Assert.Equal("me", PhishingReportRules.ReporterIdentity(identities, "alice@example.org")!.Id);
        Assert.Equal("o", PhishingReportRules.ReporterIdentity(identities, "someone@example.org")!.Id);
        Assert.Null(PhishingReportRules.ReporterIdentity([identities[0]], "alice@example.org"));
    }
}
