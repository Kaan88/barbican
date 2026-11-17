using Barbican.Core.Editor;

namespace Barbican.Core.Tests.Editor;

public sealed class QuotedHtmlSanitizerTests
{
    private const string Hostile =
        "<p onclick=\"x()\" style=\"color:#ff0000\" ONMOUSEOVER=y>Hello <b>bold</b></p>"
        + "<iframe src=\"http://evil\">frame text</iframe><object data=\"x.swf\">obj</object><embed src=\"x\">"
        + "<form action=\"javascript:alert(1)\"><input name=a>form kept</form>"
        + "<base href=\"http://evil/\"><meta http-equiv=\"refresh\" content=\"0;url=http://evil\"><link rel=\"stylesheet\" href=\"http://evil/c.css\">"
        + "<a href=\"javascript:alert(1)\">js</a><a href=\" JaVa\tScRiPt:alert(1)\">split</a><a href=\"java&#115;cript:alert(1)\">ent</a><a href=\"vbscript:x\">vb</a>"
        + "<a href=\"data:text/html;base64,PHNjcmlwdD4=\">data</a><img src=\"javascript:x\" alt=\"i\"><a href=\"https://ok.example/\">fine</a>"
        + "<img src=\"data:image/png;base64,AAAA\" onerror=\"z()\"><div style=\"width:expression(alert(1))\">expr</div>";

    private static readonly string[] Bad =
        ["onclick", "onmouseover", "onerror", "<iframe", "<object", "<embed", "<form", "<base", "<meta", "<link", "javascript:", "vbscript:", "data:text/html", "expression(", "<script"];

    [Fact]
    public void CleanQuotedHtml_strips_hostile_markup_and_keeps_content()
    {
        var h = DocHtml.CleanQuotedHtml(Hostile);
        foreach (var b in Bad)
        {
            Assert.DoesNotContain(b, h, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("Hello", h, StringComparison.Ordinal);
        Assert.Contains("<b>bold</b>", h, StringComparison.Ordinal);
        Assert.Contains("color:#ff0000", h, StringComparison.Ordinal);
        Assert.Contains("form kept", h, StringComparison.Ordinal);
        Assert.Contains("https://ok.example/", h, StringComparison.Ordinal);
        Assert.Contains("data:image/png;base64,AAAA", h, StringComparison.Ordinal);
        Assert.Contains(">js</a>", h, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Export_sanitises_a_quoted_block_even_when_it_was_built_unclean(bool cite)
    {
        var doc = new Doc([Block.Para("reply"), new Block(new BlockKind.Quoted(Hostile, "t", false, cite))]);
        var html = DocHtml.Export(doc);
        foreach (var b in Bad)
        {
            Assert.DoesNotContain(b, html, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("reply", html, StringComparison.Ordinal);
        Assert.Contains("Hello", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ImportWithQuotes_sanitises_the_reopened_draft_quote()
    {
        var blocks = DocHtml.ImportWithQuotes("<p>mine</p><blockquote type=\"cite\">" + Hostile + "</blockquote>");
        var q = Assert.IsType<BlockKind.Quoted>(blocks[^1].Kind);
        foreach (var b in Bad)
        {
            Assert.DoesNotContain(b, q.Html, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Clean_input_is_returned_byte_for_byte()
    {
        const string clean = "<p style=\"color:#111\">a <a href=\"https://x.example/?a=1&amp;b=2\">l</a><br/></p>";
        Assert.Equal(clean, DocHtml.CleanQuotedHtml(clean));
    }
}
