// Mirrors: Rust compose.rs:109 cid_html, :117 inline_parts; Bulwark lib/email-composer-utils.ts collectInlineImageCids; RFC 2392 (cid URLs).

using Barbican.Core.Mail.Model;

namespace Barbican.Core.Compose;

/// <summary>Quoted inline images: the original's HTML with its <c>cid:</c> references, and the parts to resend.</summary>
public static class CidHtml
{
    /// <summary>The HTML body with its original <c>cid:</c> image references (<see cref="EmailBody.CidBodyValues"/> by part id, else the body value), parts joined by a newline.</summary>
    public static string Of(EmailBody message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var parts = new List<string>();
        foreach (var p in message.HtmlBody)
        {
            if (p.PartId is not { } id)
            {
                continue;
            }

            if (message.CidBodyValues.TryGetValue(id, out var cid))
            {
                parts.Add(cid);
            }
            else if (message.BodyValues.TryGetValue(id, out var v))
            {
                parts.Add(v.Value);
            }
        }

        return string.Join('\n', parts);
    }

    /// <summary>The cid attachments of <paramref name="message"/> that <paramref name="html"/> references: (content id without brackets, the part as a draft attachment).</summary>
    public static IReadOnlyList<(string Cid, DraftAttachment Part)> InlineParts(EmailBody message, string html)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(html);
        var list = new List<(string, DraftAttachment)>();
        foreach (var a in message.Attachments)
        {
            if (a.Cid is not { } raw)
            {
                continue;
            }

            var cid = raw.Trim('<', '>');
            if (!html.Contains("cid:" + cid, StringComparison.Ordinal))
            {
                continue;
            }

            list.Add((cid, new DraftAttachment
            {
                BlobId = a.BlobId ?? "",
                Name = a.Name ?? "image",
                Type = a.Type ?? "application/octet-stream",
                Size = a.Size,
                Cid = cid,
            }));
        }

        return list;
    }
}
