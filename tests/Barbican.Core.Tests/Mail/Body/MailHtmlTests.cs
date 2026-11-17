// Rust main.rs body_text :664, html_to_text :578, has_remote_images :129 (tag rust-final). Owner body.

using Barbican.Core.Mail.Body;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Tests.Mail.Body;

public sealed class MailHtmlTests
{
    [Fact]
    public void Body_text_joins_the_parts_by_part_id()
    {
        var m = new EmailBody
        {
            Id = "1",
            HtmlBody = [new BodyPart { PartId = "a", Type = "text/html" }, new BodyPart { PartId = "b", Type = "text/html" }],
            TextBody = [new BodyPart { PartId = "c", Type = "text/plain" }, new BodyPart { PartId = "missing" }],
            BodyValues = new Dictionary<string, BodyValue>(StringComparer.Ordinal) { ["a"] = new("<p>1</p>"), ["b"] = new("<p>2</p>"), ["c"] = new("text") },
        };
        var (html, text) = MailHtml.BodyText(m);
        Assert.Equal("<p>1</p>\n<p>2</p>", html);
        Assert.StartsWith("text", text, StringComparison.Ordinal);
        Assert.True(MailHtml.IsHtml(m));
        Assert.False(MailHtml.IsHtml(m with { HtmlBody = [new BodyPart { PartId = "c", Type = "text/plain" }] }));
    }

    [Fact]
    public void Html_to_text_drops_tags_styles_and_extra_blank_lines()
    {
        var t = MailHtml.HtmlToText("<head><title>t</title></head><style>p{}</style><p>Hello&nbsp;<b>you</b></p><br><br><br><br><div>a &amp; b</div>");
        Assert.DoesNotContain("p{}", t, StringComparison.Ordinal);
        Assert.DoesNotContain("<", t, StringComparison.Ordinal);
        Assert.StartsWith("Hello you", t, StringComparison.Ordinal);
        Assert.EndsWith("a & b", t, StringComparison.Ordinal);
        Assert.DoesNotContain("\n\n\n", t, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<img src=\"https://x.example.com/a.png\">", true)]
    [InlineData("<td background='http://x.example.com/b.png'>", true)]
    [InlineData("<div style=\"background:url(//x.example.com/c)\">", true)]
    [InlineData("<video poster=\"https://x.example.com/p.jpg\">", true)]
    [InlineData("<img src=\"data:image/png;base64,AAAA\">", false)]
    [InlineData("<a href=\"https://x.example.com\">link</a>", false)]
    public void Remote_content(string html, bool remote) => Assert.Equal(remote, MailHtml.HasRemoteContent(html));

    [Fact]
    public void Plain_view_shows_the_text_or_the_html_as_text()
    {
        Assert.Equal("<p>x</p>", new MailContent("<p>x</p>", "x").BodyMarkup(plainView: false));
        Assert.Equal("<pre>x &lt;y&gt;</pre>", new MailContent("<p>q</p>", "x <y>").BodyMarkup(plainView: true));
        Assert.Equal("<pre>q</pre>", new MailContent("<p>q</p>", string.Empty).BodyMarkup(plainView: true));
        Assert.Equal("<pre>t</pre>", new MailContent(null, "t").BodyMarkup(plainView: false));
    }
}
