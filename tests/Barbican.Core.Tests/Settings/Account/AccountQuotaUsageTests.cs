// Not Rust rows: G-057 (storage usage on the Account page: "3.5 MB of 1 GB used", Bulwark account-settings.tsx). M6 stream set-data.

using Barbican.Core.Settings.Account;

namespace Barbican.Core.Tests.Settings.Account;

public sealed class AccountQuotaUsageTests
{
    [Fact]
    [Red("set-data")]
    public void Usage_reads_as_a_fraction_and_a_sentence()
    {
        var usage = new AccountQuotaUsage(500_000_000, 1_000_000_000);
        Assert.Equal(0.5, usage.Fraction, 3);
        Assert.Equal("500 MB of 1 GB used", usage.Text);
        Assert.Equal(1.0, new AccountQuotaUsage(2_000_000_000, 1_000_000_000).Fraction, 3);
        var open = new AccountQuotaUsage(3_500_000, null);
        Assert.Equal(-1.0, open.Fraction, 3);
        Assert.Equal("3.5 MB used", open.Text);
    }
}
