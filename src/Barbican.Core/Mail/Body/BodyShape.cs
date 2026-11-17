// Mirrors: RFC 8621 §4.1 (Email properties such as bcc, replyTo, sender are "EmailAddress[]|null": a server sends null for an absent
// header). The JSON reader keeps such a null in the non-nullable list properties of EmailBody; the reader's code (and the body cache's
// weight) treat them as lists. Owner body (FABLE-REVIEW "M1 stream: body", request to jmap: read null lists as empty).

using Barbican.Core.Mail.Model;

namespace Barbican.Core.Mail.Body;

/// <summary>Gives a fetched message empty lists where the server sent null.</summary>
public static class BodyShape
{
#pragma warning disable IDE0029, IDE0270, CS8600, CS8603 // the properties are declared non-null, the JSON reader leaves null in them
    public static EmailBody Normalize(EmailBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (body.Headers is not null && body.From is not null && body.To is not null && body.Cc is not null && body.Bcc is not null && body.ReplyTo is not null
            && body.Sender is not null && body.TextBody is not null && body.HtmlBody is not null && body.Attachments is not null && body.BodyValues is not null
            && body.MailboxIds is not null && body.MessageId is not null && body.InReplyTo is not null && body.References is not null && body.Keywords is not null)
        {
            return body;
        }

        return body with
        {
            Headers = body.Headers ?? [],
            From = body.From ?? [],
            To = body.To ?? [],
            Cc = body.Cc ?? [],
            Bcc = body.Bcc ?? [],
            ReplyTo = body.ReplyTo ?? [],
            Sender = body.Sender ?? [],
            TextBody = body.TextBody ?? [],
            HtmlBody = body.HtmlBody ?? [],
            Attachments = body.Attachments ?? [],
            BodyValues = body.BodyValues ?? new Dictionary<string, BodyValue>(StringComparer.Ordinal),
            MailboxIds = body.MailboxIds ?? new Dictionary<string, bool>(StringComparer.Ordinal),
            MessageId = body.MessageId ?? [],
            InReplyTo = body.InReplyTo ?? [],
            References = body.References ?? [],
            Keywords = body.Keywords ?? Keywords.None,
        };
    }
#pragma warning restore IDE0029, IDE0270, CS8600, CS8603
}
