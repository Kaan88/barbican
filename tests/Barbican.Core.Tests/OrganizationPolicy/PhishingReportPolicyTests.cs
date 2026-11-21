// Bulwark lib/admin/organisation-policy.ts (phishingReport: address and format, a scope replaces per leaf, a bad address is dropped) and
// lib/__tests__/phishing-report.test.ts ("only while the admin set an address").
using System.Text.Json;
using Barbican.Core.OrganizationPolicy;

namespace Barbican.Core.Tests.OrganizationPolicy;

public sealed class PhishingReportPolicyTests
{
    [Fact]
    public void Reporting_is_off_until_the_admin_sets_an_address()
    {
        Assert.False(Resolve("""{"organisation":{},"scopes":[]}""").PhishingReport.IsOn);
        Assert.False(Resolve("""{"organisation":{"phishingReport":{"address":"","format":"arf"}},"scopes":[]}""").PhishingReport.IsOn);
        var on = Resolve("""{"organisation":{"phishingReport":{"address":" SOC@Example.org ","format":"arf"}},"scopes":[]}""").PhishingReport;
        Assert.Equal(new PhishingReportPolicy("soc@example.org", PhishingReportFormat.Arf), on);
    }

    [Fact]
    public void The_format_defaults_to_forward_and_an_unknown_one_is_ignored()
    {
        Assert.Equal(PhishingReportFormat.Forward, Resolve("""{"organisation":{"phishingReport":{"address":"soc@example.org"}},"scopes":[]}""").PhishingReport.Format);
        Assert.Equal(PhishingReportFormat.Forward, Resolve("""{"organisation":{"phishingReport":{"address":"soc@example.org","format":"zip"}},"scopes":[]}""").PhishingReport.Format);
    }

    [Theory]
    [InlineData("not an address")]
    [InlineData("a@b")]
    [InlineData("a b@example.org")]
    [InlineData("<a@example.org>")]
    public void An_address_that_is_not_one_is_dropped_never_used_and_never_breaks_the_rest_of_the_policy(string address)
    {
        var effective = Resolve("{\"organisation\":{\"devices\":{\"noPrint\":true},\"phishingReport\":{\"address\":\"" + address + "\"}},\"scopes\":[]}");

        Assert.False(effective.PhishingReport.IsOn);
        Assert.True(effective.NoPrint);
    }

    [Fact]
    public void A_scope_replaces_the_address_and_the_format_each_on_their_own()
    {
        const string json = """
        {"organisation":{"phishingReport":{"address":"global@example.org","format":"arf"}},
         "scopes":[
          {"id":"eu","match":{"domains":["example.org"]},"policy":{"organisation":{"phishingReport":{"address":"eu@example.org"}}}},
          {"id":"off","match":{"domains":["other.example.org"]},"policy":{"organisation":{"phishingReport":{"address":""}}}}
         ]}
        """;

        var eu = Resolve(json, "me@example.org").PhishingReport;
        Assert.Equal(new PhishingReportPolicy("eu@example.org", PhishingReportFormat.Arf), eu);
        Assert.False(Resolve(json, "me@other.example.org").PhishingReport.IsOn);
        Assert.Equal("global@example.org", Resolve(json, "me@elsewhere.example.net").PhishingReport.Address);
    }

    private static EffectiveOrganizationPolicy Resolve(string json, string account = "user@example.org")
    {
        using var document = JsonDocument.Parse(json);
        var parsed = OrganizationPolicyParser.Parse(document.RootElement);
        Assert.True(parsed.IsOk);
        return OrganizationPolicyResolver.Resolve(parsed.Value!, new(account, []));
    }
}
