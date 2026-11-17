// Mirrors: Rust compose.rs:109 cid_html, :117 inline_parts; Bulwark lib/email-composer-utils.ts collectInlineImageCids; RFC 2392 (cid URLs). Contract stub from the M3 plan section 4.3; owner send-pipeline replaces the bodies.

using Barbican.Core.Common;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Compose;

/// <summary>Quoted inline images: the original's HTML with its <c>cid:</c> references, and the parts to resend (stub until stream send-pipeline lands).</summary>
public static class CidHtml
{
    /// <summary>The HTML body with its original <c>cid:</c> image references (<see cref="EmailBody.CidBodyValues"/> by part id, else the body value), parts joined by a newline.</summary>
    public static string Of(EmailBody message) => throw new PortPendingException("CidHtml.Of");

    /// <summary>The cid attachments of <paramref name="message"/> that <paramref name="html"/> references: (content id without brackets, the part as a draft attachment).</summary>
    public static IReadOnlyList<(string Cid, DraftAttachment Part)> InlineParts(EmailBody message, string html) => throw new PortPendingException("CidHtml.InlineParts");
}
