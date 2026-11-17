// Bulwark lib/auth/__tests__/mail-server-discovery.test.ts (emailDomain, unsupportedProvider, normalizeServerInput, serverHostLabel). spec
// I-098, I-099, I-101.

using Barbican.Core.Accounts;

namespace Barbican.Core.Tests.Accounts;

public sealed class MailServerAddressTests
{
    [Theory]
    [InlineData("user@example.org", "example.org")]
    [InlineData("  User@Example.ORG. ", "example.org")]
    [InlineData("user@", "")]
    [InlineData("user@example", "")]
    [InlineData("@example.org", "")]
    [InlineData("user", "")]
    public void Email_domain(string input, string domain) => Assert.Equal(domain, MailServerAddress.EmailDomain(input));

    [Theory]
    [InlineData("outlook.com", "Outlook.com")]
    [InlineData("hotmail.de", "Outlook.com")]
    [InlineData("live.co.uk", "Outlook.com")]
    [InlineData("icloud.com", "iCloud Mail")]
    [InlineData("ME.com", "iCloud Mail")]
    [InlineData("example.org", null)]
    [InlineData("outlookish.com", null)]
    public void Unsupported_providers(string domain, string? name) => Assert.Equal(name, MailServerAddress.UnsupportedProvider(domain));

    [Theory]
    [InlineData("mail.example.org", "https://mail.example.org")]
    [InlineData("mail.example.org:8443", "https://mail.example.org:8443")]
    [InlineData("https://mail.example.org/", "https://mail.example.org")]
    [InlineData("http://localhost:8080/jmap/", "http://localhost:8080/jmap")]
    [InlineData("ftp://mail.example.org", null)]
    [InlineData("mail example.org", null)]
    [InlineData("https://u:p@mail.example.org", null)]
    [InlineData("", null)]
    public void Server_input(string input, string? url) => Assert.Equal(url, MailServerAddress.NormalizeServerInput(input));

    [Theory]
    [InlineData("https://mail.example.org", "mail.example.org")]
    [InlineData("https://mail.example.org:8443", "mail.example.org:8443")]
    [InlineData("http://localhost:8080", "http://localhost:8080")]
    [InlineData("not a url", "not a url")]
    public void Host_label(string url, string label) => Assert.Equal(label, MailServerAddress.ServerHostLabel(url));
}
