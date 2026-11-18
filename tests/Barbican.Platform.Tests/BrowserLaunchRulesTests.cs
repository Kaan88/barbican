// What the system launchers hand to the operating system: web addresses and plain tel: numbers (I-205, D-M5-I06), nothing else.

namespace Barbican.Platform.Tests;

public sealed class BrowserLaunchRulesTests
{
    [Theory]
    [InlineData("https://example.org/a", true)]
    [InlineData("http://example.org", true)]
    [InlineData("tel:+442079460002", true)]
    [InlineData("tel:0207946000", true)]
    [InlineData("tel:12", false)]
    [InlineData("tel:+4420794600021234567890", false)]
    [InlineData("tel:123;ext=4", false)]
    [InlineData("tel:+44-20-7946", false)]
    [InlineData("file:///C:/Windows/notepad.exe", false)]
    [InlineData("mailto:a@example.org", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("ms-settings:privacy", false)]
    public void Only_web_addresses_and_plain_phone_numbers_pass(string uri, bool allowed) =>
        Assert.Equal(allowed, BrowserLaunchRules.Allows(new Uri(uri, UriKind.Absolute)));

    [Fact]
    public void A_relative_address_never_passes() => Assert.False(BrowserLaunchRules.Allows(new Uri("/x", UriKind.Relative)));
}
