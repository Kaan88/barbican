// Mirrors: Bulwark lib/__tests__/plugin-link-params.test.ts and desktop os-commands.ts.
using Barbican.Core.Mailto;

namespace Barbican.Core.Tests.Mailto;

public sealed class ComposeLinkParametersTests
{
    [Fact]
    public void MailtoKeepsFirstDecodedParameterAndBoundsUntrustedInput()
    {
        var link = MailtoLink.Parse("mailto:a@example.org?subject=Hi&X-Akte-Case=01%2F2026&x-akte-case=second&x-akte-bad=%00a%0Db%7F&x-akte-%3Cb%3E=no")!;
        Assert.Equal("Hi", link.Subject, StringComparer.Ordinal);
        Assert.Equal("01/2026", link.Extra["x-akte-case"], StringComparer.Ordinal);
        Assert.Equal("ab", link.Extra["x-akte-bad"], StringComparer.Ordinal);
        Assert.Equal(2, link.Extra.Count);
        var many = Enumerable.Range(0, 80).Select(i => KeyValuePair.Create("x-akte-p" + i, new string('v', 3000)));
        var parsed = ComposeLinkParameters.Parse(many);
        Assert.Equal(50, parsed.Count);
        Assert.All(parsed.Values, value => Assert.Equal(2048, value.Length));
    }

    [Fact]
    public void LongestOwnerAloneReceivesEachParameterIncludingReservedNames()
    {
        var input = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["x-e-invoice-number"] = "2", ["x-e-other"] = "3", ["x-unknown-a"] = "4",
            ["x-e-invoice-"] = "5", ["x-e-invoice-__proto__"] = "6", ["x-e-invoice-constructor"] = "7",
        };
        var result = ComposeLinkParameters.Partition(input, ["e", "e-invoice"]);
        Assert.Equal("2", result["e-invoice"]["number"], StringComparer.Ordinal);
        Assert.Equal("6", result["e-invoice"]["__proto__"], StringComparer.Ordinal);
        Assert.Equal("7", result["e-invoice"]["constructor"], StringComparer.Ordinal);
        Assert.Equal("3", result["e"]["other"], StringComparer.Ordinal);
        Assert.Equal("5", result["e"]["invoice-"], StringComparer.Ordinal);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void DesktopNewMessageUriCarriesOnlyPluginParameters()
    {
        var link = MailtoLink.ParseComposeUri("bulwark://new/?subject=ignored&x-akte-case=01%2F2026&x-akte-name=A+B")!;
        Assert.Equal("", link.Subject, StringComparer.Ordinal);
        Assert.Equal("01/2026", link.Extra["x-akte-case"], StringComparer.Ordinal);
        Assert.Equal("A B", link.Extra["x-akte-name"], StringComparer.Ordinal);
        Assert.Null(MailtoLink.Parse("bulwark://new?x-akte-case=1"));
        Assert.Null(MailtoLink.ParseComposeUri("bulwark://new/other?x-akte-case=1"));
        Assert.Null(MailtoLink.ParseComposeUri("bulwark://user@new?x-akte-case=1"));
        Assert.Null(MailtoLink.ParseComposeUri("bulwark://new:99?x-akte-case=1"));
    }
}
