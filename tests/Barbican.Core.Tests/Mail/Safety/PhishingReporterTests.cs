// Bulwark lib/__tests__/phishing-report.test.ts and jmap-phishing-report-send.test.ts: one report per message, to the admin's address only, from the login's
// own identity, no copy in Sent; over the in-process fake server (nothing leaves the process, the address is a reserved example domain).
using System.Text;
using Barbican.Core.Compose;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Safety;
using Barbican.Core.OrganizationPolicy;
using Barbican.Core.Tests.Compose;

namespace Barbican.Core.Tests.Mail.Safety;

public sealed class PhishingReporterTests
{
    private const string Admin = "security@example.org";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(PhishingReportFormat.Forward)]
    [InlineData(PhishingReportFormat.Arf)]
    public async Task One_report_goes_to_the_admin_address_with_the_original_inside_and_no_copy_in_Sent(PhishingReportFormat format)
    {
        var rig = await PipelineRig.StartAsync();
        var id = rig.Store.EmailIds()[0];
        var subject = (string?)rig.Store.Email(id)!["subject"];
        var blob = (string?)rig.Store.Email(id)!["blobId"];
        var original = rig.Store.Blob(blob!)!;
        var sentBefore = rig.InFolder("sent").Count;

        var result = await Reporter(rig).ReportAsync(Target(id), new PhishingReportPolicy(Admin, format), Ct);

        Assert.True(result.IsOk, result.Error?.Message);
        var submission = Assert.Single(rig.Api.Submissions);
        Assert.Equal(Admin, (string?)submission["envelope"]?["rcptTo"]?[0]?["email"]);
        Assert.Single(submission["envelope"]!["rcptTo"]!.AsArray());
        Assert.Equal("user@example.org", (string?)submission["envelope"]?["mailFrom"]?["email"]);
        Assert.Equal("i1", (string?)submission["identityId"]);
        var sent = rig.Store.Blob(rig.Fake.Server.LastUploadedBlobId!)!;
        var text = Encoding.Latin1.GetString(sent);
        Assert.Contains($"To: {Admin}\r\n", text, StringComparison.Ordinal);
        Assert.Contains("Phishing: " + subject, text, StringComparison.Ordinal);
        Assert.Contains(format == PhishingReportFormat.Arf ? "report-type=feedback-report" : "multipart/mixed", text, StringComparison.Ordinal);
        Assert.True(sent.AsSpan().IndexOf(original) > 0, "the original is inside, byte for byte");
        // submit-only: the imported copy is destroyed once submitted, nothing lands in Sent or Drafts
        Assert.Equal(sentBefore, rig.InFolder("sent").Count);
        Assert.Empty(rig.InFolder("drafts"));
    }

    [Fact]
    public async Task Nothing_is_sent_while_reporting_is_off()
    {
        var rig = await PipelineRig.StartAsync();

        var result = await Reporter(rig).ReportAsync(Target(rig.Store.EmailIds()[0]), PhishingReportPolicy.Off, Ct);

        Assert.False(result.IsOk);
        Assert.Empty(rig.Api.Submissions);
        Assert.DoesNotContain(rig.Fake.Server.Requests, call => call.StartsWith("POST upload", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_message_that_is_gone_sends_nothing()
    {
        var rig = await PipelineRig.StartAsync();

        var result = await Reporter(rig).ReportAsync(Target("does-not-exist"), new PhishingReportPolicy(Admin, PhishingReportFormat.Forward), Ct);

        Assert.False(result.IsOk);
        Assert.Empty(rig.Api.Submissions);
    }

    [Fact]
    public async Task A_message_the_server_would_refuse_to_take_is_not_downloaded()
    {
        var rig = await PipelineRig.StartAsync(PipelineRig.M3Options() with { MaxSizeUpload = 100 });
        var id = rig.Store.EmailIds()[0];

        var result = await Reporter(rig).ReportAsync(Target(id), new PhishingReportPolicy(Admin, PhishingReportFormat.Arf), Ct);

        Assert.False(result.IsOk);
        Assert.Contains("too big", result.Error!.Message, StringComparison.Ordinal);
        Assert.Empty(rig.Api.Submissions);
    }

    private static PhishingReporter Reporter(PipelineRig rig) => new(rig.Accounts, rig.Identities, new RawMessageSender(rig.Accounts, rig.Clock), rig.Clock);

    private static MailTarget Target(string id) => new(PipelineRig.Account, string.Empty, id, string.Empty, id, new Dictionary<string, bool>(StringComparer.Ordinal), Keywords.None);
}
