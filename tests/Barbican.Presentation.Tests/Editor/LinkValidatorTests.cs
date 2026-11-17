using Barbican.Presentation.Editor;

namespace Barbican.Presentation.Tests.Editor;

/// <summary>I-266 and I-267: the link dialog and picture-address validation of Bulwark.</summary>
public sealed class LinkValidatorTests
{
    [Theory]
    [InlineData("https://example.test/a b", true)]
    [InlineData("example.test/a\tb", true)]
    [InlineData("https://example.test/a", false)]
    [InlineData("  example.test  ", false)]
    [InlineData("", false)]
    public void An_address_with_a_space_shows_the_error(string href, bool error)
    {
        Assert.Equal(error ? LinkValidator.InvalidLink : null, LinkValidator.LinkError(href));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("a b", false)]
    [InlineData("example.test", true)]
    [InlineData(" mailto:someone@mail.example ", true)]
    public void Save_needs_an_address_without_spaces(string href, bool can)
    {
        Assert.Equal(can, LinkValidator.CanSubmitLink(href));
    }

    [Theory]
    [InlineData("https://example.test/logo.png", "https://example.test/logo.png")]
    [InlineData("  https://example.test/logo.png ", "https://example.test/logo.png")]
    [InlineData("http://example.test/logo.png", null)]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("data:image/png;base64,AAAA", null)]
    [InlineData("https://", null)]
    [InlineData("https://example.test/a b.png", null)]
    [InlineData("logo.png", null)]
    [InlineData("", null)]
    public void A_picture_address_is_https_with_a_host_and_nothing_else(string raw, string? expected)
    {
        Assert.Equal(expected, LinkValidator.PictureAddress(raw));
    }
}
