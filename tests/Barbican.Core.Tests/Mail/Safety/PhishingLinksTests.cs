// Bulwark lib/__tests__/phishing-report.test.ts ("links of a reported message") and lib/phishing-links.ts.
using AngleSharp.Html.Parser;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Safety;

namespace Barbican.Core.Tests.Mail.Safety;

public sealed class PhishingLinksTests
{
    private const string Title = "link turned off";
    private const string Note = "(link turned off)";

    [Fact]
    public void Links_are_turned_off_no_address_not_reachable_by_Tab_and_say_so()
    {
        const string html = "<p>Hi <a href=\"https://phish.example/login\" target=\"_blank\" rel=\"noopener\">log in</a> <map><area href=\"https://x.example\" alt=\"x\"></map></p>";

        var output = PhishingLinks.TurnOff(html, Title, Note);

        Assert.DoesNotContain("href=", output, StringComparison.Ordinal);
        Assert.DoesNotContain("phish.example", output, StringComparison.Ordinal);
        Assert.Contains("data-link-off", output, StringComparison.Ordinal);
        Assert.Contains(Note, output, StringComparison.Ordinal);
        var document = new HtmlParser().ParseDocument(output);
        Assert.Empty(document.QuerySelectorAll("a[href], area[href]"));
        Assert.Equal(2, document.QuerySelectorAll("[data-link-off]").Length);
    }

    [Fact]
    public void Every_navigation_attribute_goes_and_the_link_looks_like_text()
    {
        const string html = "<a HREF=\"https://a.example\" target=\"_top\" rel=\"x\" ping=\"https://p.example\" download=\"f\" tabindex=\"0\" style=\"color:red\">Pay</a>";

        var link = new HtmlParser().ParseDocument(PhishingLinks.TurnOff(html, Title, Note)).QuerySelector("a")!;

        foreach (var name in new[] { "href", "target", "rel", "ping", "download", "tabindex" })
        {
            Assert.False(link.HasAttribute(name), name);
        }

        Assert.Equal("true", link.GetAttribute("aria-disabled"));
        Assert.Equal(Title, link.GetAttribute("title"));
        var style = link.GetAttribute("style")!;
        Assert.StartsWith("color:red;", style, StringComparison.Ordinal);
        Assert.Contains("cursor:not-allowed !important", style, StringComparison.Ordinal);
        var note = link.QuerySelector("[data-link-off-note]")!;
        Assert.Equal(" " + Note, note.TextContent);
        Assert.Equal("Pay " + Note, link.TextContent);
    }

    [Fact]
    public void An_area_gets_no_style_or_note()
    {
        var area = new HtmlParser().ParseDocument(PhishingLinks.TurnOff("<map><area href=\"https://x.example\"></map>", Title, Note)).QuerySelector("area")!;

        Assert.False(area.HasAttribute("href"));
        Assert.True(area.HasAttribute("data-link-off"));
        Assert.False(area.HasAttribute("style"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("<p>No links, an <abbr title=\"x\">abbr</abbr> and an <article>article</article>.</p>")]
    [InlineData("plain text with a@b.example")]
    public void Html_without_a_link_comes_back_unchanged(string html) => Assert.Same(html, PhishingLinks.TurnOff(html, Title, Note));

    [Fact]
    public void A_full_document_keeps_its_head_and_a_fragment_stays_a_fragment()
    {
        var full = PhishingLinks.TurnOff("<html><head><style>p{color:blue}</style></head><body><a href=\"https://x.example\">x</a></body></html>", Title, Note);
        var fragment = PhishingLinks.TurnOff("<p><a href=\"https://x.example\">x</a></p>", Title, Note);

        Assert.Contains("<style>p{color:blue}</style>", full, StringComparison.Ordinal);
        Assert.StartsWith("<html", full, StringComparison.Ordinal);
        Assert.StartsWith("<p>", fragment, StringComparison.Ordinal);
        Assert.DoesNotContain("<html", fragment, StringComparison.Ordinal);
    }

    [Fact]
    public void The_mark_follows_the_phishing_keyword()
    {
        Assert.True(PhishingLinks.IsMarked(Keywords.Of("$phishing")));
        Assert.False(PhishingLinks.IsMarked(Keywords.Of("$junk")));
        Assert.False(PhishingLinks.IsMarked(new Keywords(new Dictionary<string, bool>(StringComparer.Ordinal) { ["$phishing"] = false })));
        Assert.False(PhishingLinks.IsMarked(null));
    }
}
