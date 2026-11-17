#pragma warning disable MA0002, MA0006 // asserts compare strings ordinally by nature (the ported Rust asserts)
using Barbican.Core.Compose;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Tests.Compose;

/// <summary>Rust src/compose.rs test quoted_inline_parts_use_cid_html, ported on the typed <see cref="EmailBody"/> instead of JSON (D-241; M3 stream send-pipeline).</summary>
public sealed class CidHtmlTests
{
    [Fact]
    [Red("send-pipeline")]
    [RustTest("compose::quoted_inline_parts_use_cid_html")]
    public void Quoted_inline_parts_use_cid_html()
    {
        var m = new EmailBody
        {
            Id = "m1",
            HtmlBody = [new BodyPart { PartId = "1" }],
            BodyValues = new Dictionary<string, BodyValue> { ["1"] = new("<img src=\"data:image/png;base64,AA\">") },
            CidBodyValues = new Dictionary<string, string> { ["1"] = "<img src=\"cid:i1@x\">" },
            Attachments =
            [
                new Attachment { Cid = "<i1@x>", BlobId = "B1", Type = "image/png", Name = "a.png", Size = 3 },
                new Attachment { BlobId = "B2", Name = "doc.pdf" },
            ],
        };
        var h = CidHtml.Of(m);
        Assert.Contains("cid:i1@x", h, StringComparison.Ordinal);
        var p = CidHtml.InlineParts(m, h);
        var one = Assert.Single(p);
        Assert.Equal("i1@x", one.Cid);
        Assert.Equal("B1", one.Part.BlobId);
    }
}
