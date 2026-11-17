// Ported from Rust src/attachments.rs tests (:114-:144). Red until the mailcore stream implements AttachmentCards.

using Barbican.Core.Mail.Attachments;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Tests.Mail.Attachments;

public sealed class AttachmentCardsTests
{
    [Fact]
    [RustTest("attachments::names_and_kinds")]
    [Red("mailcore")]
    public void Names_and_kinds()
    {
        Assert.Equal(("report.final", "PDF"), AttachmentCards.SplitName("report.final.pdf"));
        Assert.Equal(("README", ""), AttachmentCards.SplitName("README"));
        Assert.Equal((".bashrc", ""), AttachmentCards.SplitName(".bashrc"));
        Assert.Equal(("a.tar gz", ""), AttachmentCards.SplitName("a.tar gz"));
    }

    [Fact]
    [RustTest("attachments::which_attachments_preview")]
    [Red("mailcore")]
    public void Which_attachments_preview()
    {
        var img = new Attachment { Type = "image/PNG", Size = 2000, BlobId = "b" };
        var big = new Attachment { Type = "image/jpeg", Size = 20_000_000, BlobId = "b" };
        var svg = new Attachment { Type = "image/svg+xml", Size = 10, BlobId = "b" };
        var pdf = new Attachment { Type = "application/pdf", Size = 10, BlobId = "b" };
        Assert.True(AttachmentCards.Previewable(img));
        Assert.False(AttachmentCards.Previewable(big));
        Assert.False(AttachmentCards.Previewable(svg));
        Assert.False(AttachmentCards.Previewable(pdf));
        Assert.True(AttachmentCards.Wanted([pdf, img], true));
        Assert.False(AttachmentCards.Wanted([pdf, img], false));
        Assert.False(AttachmentCards.Wanted([pdf], true));
        Assert.Equal("2 attachments · 2 KB", AttachmentCards.Summary([new Attachment { Size = 1500 }, new Attachment { Size = 500 }]));
    }

    [Fact]
    [RustTest("attachments::thumbnails_are_square")]
    [Red("mailcore")]
    public void Thumbnails_are_square()
    {
        var png = TestPng.Solid(120, 60, 10, 20, 30, 255);
        var t = AttachmentCards.Thumbnail(png);
        Assert.NotNull(t);
        Assert.Equal((AttachmentCards.ThumbnailEdge, AttachmentCards.ThumbnailEdge), (t.Width, t.Height));
        Assert.Null(AttachmentCards.Thumbnail("not an image"u8.ToArray()));
    }
}
