// Mirrors: Bulwark onEmailOpen payload, lib/plugin-projection.ts:49 emailToReadView (EmailReadView), fired from stores/email-store.ts:4005
// finishOpenEmail with the full message. Server-set keywords are not filtered here (Stalwart sets none the reader shows). Owner body.

using Barbican.Core.Mail.Model;

namespace Barbican.Core.Mail.Body.Hooks;

/// <summary>What a plugin hears about a message that was opened (plain data, no live objects).</summary>
public sealed record EmailReadView(
    string Id,
    string? ThreadId,
    IReadOnlyList<string> MailboxIds,
    IReadOnlyList<EmailAddress> From,
    IReadOnlyList<EmailAddress> To,
    IReadOnlyList<EmailAddress> Cc,
    string Subject,
    DateTimeOffset? ReceivedAt,
    bool IsRead,
    bool IsFlagged,
    bool HasAttachment,
    IReadOnlyList<Attachment> Attachments,
    string Preview,
    string Text,
    IReadOnlyList<string> Keywords)
{
    /// <summary>The projection of a loaded message (Bulwark emailToReadView).</summary>
    public static EmailReadView Of(EmailBody m)
    {
        ArgumentNullException.ThrowIfNull(m);
        var (html, text) = MailHtml.BodyText(m);
        return new EmailReadView(
            m.Id,
            m.ThreadId,
            [.. m.MailboxIds.Where(k => k.Value).Select(k => k.Key)],
            m.From,
            m.To,
            m.Cc,
            m.Subject ?? string.Empty,
            m.ReceivedAt,
            m.Keywords.Seen,
            m.Keywords.Flagged,
            m.HasAttachment,
            [.. m.Attachments.Where(a => a.BlobId is not null)],
            m.Preview ?? string.Empty,
            string.IsNullOrWhiteSpace(text) && html.Length > 0 ? MailHtml.HtmlToText(html) : text,
            [.. m.Keywords.Map.Where(k => k.Value).Select(k => k.Key)]);
    }
}
