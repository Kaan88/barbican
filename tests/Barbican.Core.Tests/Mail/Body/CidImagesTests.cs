// Rust main.rs:617 inline_cid_images (tag rust-final): cid: references become data: URIs, the original HTML is kept, large or unsafe
// parts stay references. Owner body.

using Barbican.Core.Mail.Body;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Tests.Mail.Body;

public sealed class CidImagesTests
{
    private static EmailBody Message(params Attachment[] parts) => new()
    {
        Id = "1",
        HtmlBody = [new BodyPart { PartId = "h", Type = "text/html" }],
        BodyValues = new Dictionary<string, BodyValue>(StringComparer.Ordinal) { ["h"] = new("<img src=\"cid:a@x\"><img src=\"cid:b@x\"><img src=\"cid:c@x\">") },
        Attachments = parts,
    };

    [Fact]
    public async Task Cid_references_become_data_uris_and_the_original_is_kept()
    {
        var m = Message(
            new Attachment { BlobId = "A", Cid = "<a@x>", Type = "image/png", Size = 3 },
            new Attachment { BlobId = "B", Cid = "<b@x>", Type = "image/png", Size = 6_000_000 },
            new Attachment { BlobId = "C", Cid = "<c@x>", Type = "image/png\" onerror=\"x", Size = 3 });
        var asked = new List<string>();
        var r = await CidImages.InlineAsync(
            m,
            (a, _) =>
            {
                asked.Add(a.BlobId!);
                return Task.FromResult<byte[]?>([1, 2, 3]);
            },
            CancellationToken.None);

        Assert.Equal(["A"], asked);
        var html = r.BodyValues["h"].Value;
        Assert.Contains("src=\"data:image/png;base64,AQID\"", html, StringComparison.Ordinal);
        Assert.Contains("cid:b@x", html, StringComparison.Ordinal);
        Assert.Contains("cid:c@x", html, StringComparison.Ordinal);
        Assert.Equal(m.BodyValues["h"].Value, r.CidBodyValues["h"]);
    }

    [Fact]
    public async Task A_failed_download_keeps_the_reference_and_no_parts_means_no_change()
    {
        var m = Message(new Attachment { BlobId = "A", Cid = "<a@x>", Type = "image/png", Size = 3 });
        var r = await CidImages.InlineAsync(m, (_, _) => Task.FromResult<byte[]?>(null), CancellationToken.None);
        Assert.Contains("cid:a@x", r.BodyValues["h"].Value, StringComparison.Ordinal);
        var none = Message();
        Assert.Same(none, await CidImages.InlineAsync(none, (_, _) => Task.FromResult<byte[]?>(null), CancellationToken.None));
    }
}
