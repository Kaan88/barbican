// Fable review 1 F-03 point 3: active and navigating markup is removed before a page is built (Bulwark lib/email-sanitization.ts runs
// DOMPurify; Rust had nothing). Owner body (fix-security stream).

using Barbican.Core.Mail.Body;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Tests.Mail.Body;

public sealed class MailSanitizerTests
{
    [Theory]
    [InlineData("<p>x</p><script>alert(1)</script>", "<script")]
    [InlineData("<meta http-equiv=\"refresh\" content=\"0;url=https://x.example.com/\"><p>x</p>", "refresh")]
    [InlineData("<link rel=\"dns-prefetch\" href=\"//tracker.example.com\"><p>x</p>", "<link")]
    [InlineData("<link rel=\"stylesheet\" href=\"https://x.example.com/a.css\"><p>x</p>", "a.css")]
    [InlineData("<base href=\"https://x.example.com/\"><a href=\"y\">y</a>", "<base")]
    [InlineData("<iframe src=\"https://x.example.com/\"></iframe><p>x</p>", "<iframe")]
    [InlineData("<object data=\"https://x.example.com/x.swf\"></object><embed src=\"x.swf\"><p>x</p>", "x.swf")]
    [InlineData("<img src=\"data:image/png;base64,AA\" onerror=\"alert(1)\">", "onerror")]
    [InlineData("<body onload=\"alert(1)\"><p>x</p></body>", "onload")]
    [InlineData("<a href=\"https://x.example.com/\" ping=\"https://t.example.com/\">x</a>", "ping=")]
    [InlineData("<a href=\" JaVa\tScRiPt:alert(1)\">x</a>", "script:")]
    [InlineData("<svg><script>alert(1)</script><a xlink:href=\"javascript:alert(1)\"><text>x</text></a></svg>", "alert")]
    [InlineData("<form action=\"https://x.example.com/\"><input name=q><button formaction=\"https://y.example.com/\">go</button></form>", "example.com")]
    public void Active_and_navigating_markup_is_removed(string html, string gone)
    {
        var clean = MailSanitizer.Clean(html, out var changed);
        Assert.True(changed);
        Assert.DoesNotContain(gone, clean, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_form_is_unwrapped_and_its_content_stays()
    {
        var clean = MailSanitizer.Clean("<form action=\"https://x.example.com/\"><p>Read me</p><input name=q></form>", out _);
        Assert.DoesNotContain("<form", clean, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<p>Read me</p>", clean, StringComparison.Ordinal);
        Assert.Contains("<input name=\"q\">", clean, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<p style=\"color:red\">Hello <b>you</b></p><img src=\"https://img.example.com/a.png\" width=\"10\">")]
    [InlineData("<html><head><style>td{color:#333}</style></head><body bgcolor=\"#fff\"><table><tr><td>x</td></tr></table></body></html>")]
    [InlineData("<pre>plain &lt;text&gt;</pre>")]
    [InlineData("")]
    public void Ordinary_mail_is_returned_untouched(string html)
    {
        var clean = MailSanitizer.Clean(html, out var changed);
        Assert.False(changed);
        Assert.Same(html, clean);
    }

    [Fact]
    public void A_whole_document_stays_a_document_and_keeps_its_body_attributes()
    {
        var clean = MailSanitizer.Clean("<html><head><meta charset=\"x\"><style>p{}</style></head><body bgcolor=\"#eee\"><p>x</p></body></html>", out var changed);
        Assert.True(changed);
        Assert.StartsWith("<html>", clean, StringComparison.Ordinal);
        Assert.Contains("<body bgcolor=\"#eee\">", clean, StringComparison.Ordinal);
        Assert.Contains("<style>p{}</style>", clean, StringComparison.Ordinal);
        Assert.DoesNotContain("<meta", clean, StringComparison.Ordinal);
    }

    [Fact]
    public void A_fragment_stays_a_fragment()
    {
        var clean = MailSanitizer.Clean("<style>p{}</style><p onclick=\"x()\">x</p>", out _);
        Assert.Equal("<style>p{}</style><p>x</p>", clean);
    }

    [Fact]
    public void A_fetched_body_is_cleaned_part_by_part_and_keeps_the_original()
    {
        var body = new EmailBody
        {
            Id = "m1",
            HtmlBody = [new BodyPart { PartId = "1", Type = "text/html" }, new BodyPart { PartId = "2", Type = "text/plain" }],
            BodyValues = new Dictionary<string, BodyValue>(StringComparer.Ordinal)
            {
                ["1"] = new("<p>x</p><script>s()</script>"),
                ["2"] = new("<script> is just text here"),
            },
        };
        var clean = MailSanitizer.Apply(body);
        Assert.Equal("<p>x</p>", clean.BodyValues["1"].Value);
        Assert.Equal("<script> is just text here", clean.BodyValues["2"].Value);
        Assert.Equal("<p>x</p><script>s()</script>", clean.CidBodyValues["1"]);
        Assert.Same(clean, MailSanitizer.Apply(clean));
    }

    [Fact]
    public void The_page_builder_and_the_sanitizer_together_leave_no_refresh_and_no_script()
    {
        var html = MailSanitizer.Clean("<meta http-equiv=refresh content=\"0;url=https://x.example.com/\"><p onmouseover=\"x()\">x</p>", out _);
        var page = MailPageBuilder.Build(html, new MailPageOptions { Dark = true, ReaderBackground = "#141414" });
        Assert.DoesNotContain("refresh", page.Html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onmouseover", page.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", page.Html, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("URL")]
    [InlineData("body")]
    [InlineData("documentElement")]
    [InlineData("querySelectorAll")]
    [InlineData("addEventListener")]
    [InlineData("__bbd")]
    public void An_image_cannot_name_a_member_of_its_document(string member)
    {
        // Fable review 2 R2-04: <img name="body"> is document.body inside the frame (WHATWG HTML named access on Document)
        var clean = MailSanitizer.Clean($"<p>x</p><img src=\"cid:a\" name=\"{member}\" id=\"{member}\" alt=\"a\">", out var changed);
        Assert.True(changed);
        Assert.DoesNotContain("name=", clean, StringComparison.Ordinal);
        Assert.DoesNotContain("id=", clean, StringComparison.Ordinal);
        Assert.Contains("alt=\"a\"", clean, StringComparison.Ordinal);
    }

    [Fact]
    public void An_anchor_name_and_an_image_id_without_a_name_stay()
    {
        // neither is a member of the document: in-mail anchors keep working
        const string html = "<a name=\"top\"></a><img src=\"cid:a\" id=\"logo\"><a href=\"#top\">up</a>";
        Assert.Equal(html, MailSanitizer.Clean(html, out var changed));
        Assert.False(changed);
    }
}
