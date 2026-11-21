// Mirrors: Bulwark lib/email-source.ts:11-142 generateEmailSource (the text of "View source", handed to plugins as EmailReadView.source)
// and lib/utils.ts:418 formatFileSize. The Authentication-Results and spam-score sections are left out: Barbican only trusts a header
// whose authserv-id the account's server owns (AuthServTrust), so unverified verdicts are not given to plugins. The text is cut at
// SourceCut.DisplayLimit like the reader's source dialog so one message cannot make an unbounded plugin value.
using System.Globalization;
using System.Text;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Reader;

namespace Barbican.Core.Plugins.Api.Mail;

internal static class PluginEmailSource
{
    internal static string Generate(EmailBody email)
    {
        var s = new StringBuilder();
        s.Append("=== EMAIL HEADERS ===\n\n");
        if ((email.MessageId ?? []).Count > 0) s.Append("Message-ID: ").Append(string.Join(',', (email.MessageId ?? []))).Append('\n');
        Addresses(s, "From", (email.From ?? []));
        Addresses(s, "To", (email.To ?? []));
        Addresses(s, "Cc", (email.Cc ?? []));
        Addresses(s, "Bcc", (email.Bcc ?? []));
        Addresses(s, "Reply-To", (email.ReplyTo ?? []));
        if (!string.IsNullOrEmpty(email.Subject)) s.Append("Subject: ").Append(email.Subject).Append('\n');
        if (email.SentAt is { } sent) s.Append("Date: ").Append(Utc(sent)).Append('\n');
        if (email.ReceivedAt is { } received) s.Append("Received-At: ").Append(Utc(received)).Append('\n');
        if ((email.InReplyTo ?? []).Count > 0) s.Append("In-Reply-To: ").Append(string.Join(", ", (email.InReplyTo ?? []))).Append('\n');
        if ((email.References ?? []).Count > 0) s.Append("References: ").Append(string.Join(", ", (email.References ?? []))).Append('\n');
        if ((email.Headers ?? []).Count > 0)
        {
            s.Append("\n--- Additional Headers ---\n");
            foreach (var group in (email.Headers ?? []).GroupBy(h => h.Name, StringComparer.Ordinal))
            {
                s.Append(group.Key).Append(": ").Append(string.Join("\n    ", group.Select(h => h.Value))).Append('\n');
            }
        }

        s.Append("\n=== EMAIL METADATA ===\n\n");
        s.Append("Email ID: ").Append(email.Id).Append('\n');
        s.Append("Thread ID: ").Append(email.ThreadId).Append('\n');
        s.Append("Size: ").Append(FormatSize(email.Size)).Append('\n');
        s.Append("Has Attachment: ").Append(email.HasAttachment ? "Yes" : "No").Append('\n');
        var keywords = PluginMailProjection.UserKeywords(email.Keywords);
        if (keywords.Count > 0) s.Append("Keywords: ").Append(string.Join(", ", keywords)).Append('\n');

        Attachments(s, email);
        Body(s, email);
        return SourceCut.Shown(s.ToString()).Text;
    }

    private static void Attachments(StringBuilder s, EmailBody email)
    {
        if ((email.Attachments ?? []).Count > 0)
        {
            s.Append("\n=== ATTACHMENTS ===\n\n");
            for (var i = 0; i < (email.Attachments ?? []).Count; i++)
            {
                var a = (email.Attachments ?? [])[i];
                s.Append('[').Append(i + 1).Append("] ").Append(string.IsNullOrEmpty(a.Name) ? "Unnamed" : a.Name).Append('\n');
                s.Append("    Type: ").Append(a.Type).Append('\n');
                s.Append("    Size: ").Append(FormatSize(a.Size)).Append('\n');
                s.Append("    Blob ID: ").Append(a.BlobId).Append('\n');
                if (!string.IsNullOrEmpty(a.Cid)) s.Append("    Content-ID: ").Append(a.Cid).Append('\n');
                s.Append('\n');
            }
        }
    }

    private static void Body(StringBuilder s, EmailBody email)
    {
        s.Append("\n=== EMAIL BODY ===\n\n");
        var hasBody = false;
        if (Value(email, (email.TextBody ?? [])) is { } text)
        {
            s.Append("--- Plain Text Version ---\n\n").Append(text).Append("\n\n");
            hasBody = true;
        }

        if (Value(email, (email.HtmlBody ?? [])) is { } html)
        {
            s.Append("--- HTML Version ---\n\n").Append(html).Append("\n\n");
            hasBody = true;
        }

        if (!hasBody && (email.BodyValues ?? PluginMailProjection.EmptyValues).Count > 0)
        {
            s.Append("--- Body Parts ---\n\n");
            var index = 0;
            foreach (var (key, value) in (email.BodyValues ?? PluginMailProjection.EmptyValues))
            {
                index++;
                if (string.IsNullOrWhiteSpace(value.Value)) continue;
                s.Append("Part ").Append(index).Append(" (").Append(key).Append("):\n").Append(value.Value).Append("\n\n");
                hasBody = true;
            }
        }

        if (!hasBody && !string.IsNullOrEmpty(email.Preview)) s.Append("--- Preview Only ---\n\n").Append(email.Preview).Append('\n');
        if (!hasBody && string.IsNullOrEmpty(email.Preview)) s.Append("(No body content available)\n");
    }

    private static string? Value(EmailBody email, IReadOnlyList<BodyPart> parts) =>
        parts.Count > 0 && parts[0].PartId is { Length: > 0 } id && (email.BodyValues ?? PluginMailProjection.EmptyValues).TryGetValue(id, out var v) && !string.IsNullOrWhiteSpace(v.Value) ? v.Value : null;

    private static void Addresses(StringBuilder s, string label, IReadOnlyList<EmailAddress> list)
    {
        if (list.Count == 0) return;
        s.Append(label).Append(": ").Append(string.Join(", ", list.Select(a => string.IsNullOrEmpty(a.Name) ? a.Email : $"{a.Name} <{a.Email}>"))).Append('\n');
    }

    private static string Utc(DateTimeOffset value) => value.UtcDateTime.ToString("ddd, dd MMM yyyy HH:mm:ss 'GMT'", CultureInfo.InvariantCulture);

    internal static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var i = Math.Min(units.Length - 1, (int)Math.Floor(Math.Log(bytes) / Math.Log(1024)));
        if (i == 0) return string.Create(CultureInfo.InvariantCulture, $"{bytes} B");
        var value = bytes / Math.Pow(1024, i);
        var rounded = value < 10 ? Math.Round(value * 10, MidpointRounding.AwayFromZero) / 10 : Math.Round(value, MidpointRounding.AwayFromZero);
        if (rounded >= 1024 && i < units.Length - 1)
        {
            i++;
            value = bytes / Math.Pow(1024, i);
            rounded = value < 10 ? Math.Round(value * 10, MidpointRounding.AwayFromZero) / 10 : Math.Round(value, MidpointRounding.AwayFromZero);
        }
        return string.Create(CultureInfo.InvariantCulture, $"{rounded} {units[i]}");
    }
}
