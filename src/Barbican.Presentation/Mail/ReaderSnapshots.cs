// Mirrors: Rust msgload.rs prefill :175 (header from the list row), attachments.rs show :80, auth.rs; Bulwark components/email/email-viewer.tsx,
// reader-attachments.tsx, external-sender-chip.tsx:120-125 (the External chip's tooltip, G-051), lib/server-auth-status.ts; R2-07 (AuthTrace: the server's own trace block, pinned authserv-id). Owner reader (M2).

using System.Globalization;
using Barbican.Core.Mail.Attachments;
using Barbican.Core.Mail.AuthResults;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Rules;
using Barbican.Presentation.Reader;

namespace Barbican.Presentation.Mail;

/// <summary>Builds what the reader shows from a list row (header only) or from a fetched message.</summary>
public static class ReaderSnapshots
{
    private const string HeaderDate = "ddd dd MMM yyyy, HH:mm";

    /// <summary>The header from the list row, shown at once while the body loads (an empty reader fills in without waiting).</summary>
    public static ReaderSnapshot Prefill(MessageRowViewModel row, ReaderOptions options)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(options);
        var s = row.Summary;
        return new ReaderSnapshot
        {
            EmailId = s.Id,
            Header = Header(s.From, s.To, s.Subject, s.ReceivedAt, s.Keywords.Flagged, options) with { Tags = RowTag.Of(s.Keywords, options.Tags) },
            Attachments = Cards(s.Attachments, options),
            AttachmentSummary = AttachmentCards.Wanted(Visible(s.Attachments, options), options.AttachmentImagePreviews) ? AttachmentCards.Summary(Visible(s.Attachments, options)) : string.Empty,
            ShowAttachmentCards = AttachmentCards.Wanted(Visible(s.Attachments, options), options.AttachmentImagePreviews),
            BodyState = BodyState.Loading,
        };
    }

    /// <summary>The whole snapshot of a fetched message: header, conversation strip, attachment cards, server checks.</summary>
    public static ReaderSnapshot FromBody(EmailBody body, ConversationThread? thread, ReaderOptions options)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(options);
        var visible = Visible(body.Attachments, options);
        var cards = AttachmentCards.Wanted(visible, options.AttachmentImagePreviews);
        // R2-07: only the server's own trace block counts (first Received, by a trusted or pinned host); the user's own mail gets no verdict
        IReadOnlyList<string> roles = [.. body.MailboxIds.Where(m => m.Value).Select(m => options.MailboxRoles.GetValueOrDefault(m.Key)).OfType<string>()];
        var own = body.Keywords.IsSet("$draft") || roles.Any(r => r is "sent" or "drafts");
        string? learned = null;
        IReadOnlyList<AuthResult> results = own || options.ServerHost is null
            ? []
            : AuthTrace.ServerResults(body.Headers, options.PinnedAuthServ, id => AuthServTrust.Trusts(options.ServerHost, id), out learned);
        var from = body.From.Count > 0 ? body.From[0].Email : string.Empty;
        var verified = from.Length > 0 && AuthenticationResults.AlignedPass(results, from);
        var failed = AuthenticationResults.Failure(results, verified);
        var domain = AuthenticationResults.DomainOf(from);
        return new ReaderSnapshot
        {
            EmailId = body.Id,
            Header = Header(body.From, body.To, body.Subject, body.ReceivedAt, body.Keywords.Flagged, options) with { Tags = RowTag.Of(body.Keywords, options.Tags) },
            Conversation = Strip(thread, body.Id, options),
            Attachments = Cards(body.Attachments, options),
            AttachmentSummary = cards ? AttachmentCards.Summary(visible) : string.Empty,
            ShowAttachmentCards = cards,
            AuthChips = [.. results.Where(r => r.Method is "dkim" or "spf" or "dmarc").Select(r => new AuthChip(r.Method, r.Result, IsAligned(r, domain)))],
            AuthVerdict = verified ? AuthVerdict.Verified : failed ? AuthVerdict.Failed : AuthVerdict.Unknown,
            LearnedAuthServ = learned,
            ServerResults = results,
            MailboxRoles = roles,
            BodyState = body.IsTruncated ? BodyState.Truncated : BodyState.Ready,
            Body = body,
        };
    }

    /// <summary>A message that could not be loaded: nothing of the previous one stays.</summary>
    public static ReaderSnapshot Failed(string emailId, string error) => new() { EmailId = emailId, BodyState = BodyState.Failed, Error = error };

    private static bool IsAligned(AuthResult r, string? fromDomain)
    {
        if (fromDomain is null || !string.Equals(r.Result, "pass", StringComparison.Ordinal))
        {
            return false;
        }

        return r.Method switch
        {
            "dkim" => AuthenticationResults.Aligned(fromDomain, r.Prop("header.d")),
            "spf" => r.Prop("smtp.mailfrom") is { } m && AuthenticationResults.Aligned(fromDomain, AuthenticationResults.DomainOf(m)),
            _ => true,
        };
    }

    private static IReadOnlyList<Attachment> Visible(IReadOnlyList<Attachment> all, ReaderOptions options) =>
        [.. all.Where(a => !(options.HideInlineImages && a.Cid is not null && (a.Type ?? string.Empty).StartsWith("image/", StringComparison.OrdinalIgnoreCase)))];

    private static List<AttachmentCard> Cards(IReadOnlyList<Attachment> all, ReaderOptions options)
    {
        var visible = Visible(all, options);
        var result = new List<AttachmentCard>(visible.Count);
        for (var i = 0; i < visible.Count; i++)
        {
            var a = visible[i];
            var (stem, kind) = AttachmentCards.SplitName(string.IsNullOrEmpty(a.Name) ? "attachment" : a.Name);
            result.Add(new AttachmentCard(i, stem, kind, ByteSize.Human(a.Size), a.BlobId, AttachmentCards.Previewable(a), null));
        }

        return result;
    }

    private static ReaderHeader Header(
        IReadOnlyList<EmailAddress> from,
        IReadOnlyList<EmailAddress> to,
        string? subject,
        DateTimeOffset? receivedAt,
        bool flagged,
        ReaderOptions options)
    {
        var first = from.Count > 0 ? from[0] : null;
        var name = !string.IsNullOrEmpty(first?.Name) ? first.Name : !string.IsNullOrEmpty(first?.Email) ? first.Email : "(unknown)";
        var external = options.ExternalSenderLabel
            ? ExternalSenderRules.ExternalSenderDomains(from.Select(a => a.Email), options.InternalDomains)
            : Array.Empty<string>();
        var date = receivedAt is { } at
            ? TimeZoneInfo.ConvertTime(at, options.Zone).ToString(HeaderDate, CultureInfo.InvariantCulture)
            : string.Empty;
        return new ReaderHeader(
            name,
            first?.Email ?? string.Empty,
            RowText.Initials(name),
            RowText.TintIndex(name),
            string.IsNullOrWhiteSpace(subject) ? "(no subject)" : subject,
            date,
            flagged,
            string.Join(", ", to.Select(a => !string.IsNullOrEmpty(a.Name) ? a.Name : a.Email)))
        {
            CompactDate = receivedAt is { } received
                ? TimeZoneInfo.ConvertTime(received, options.Zone).ToString("MMM d, HH:mm", CultureInfo.InvariantCulture)
                : string.Empty,
            ExternalSenderTooltip = external.Count == 0 ? string.Empty : ExternalSenderRules.Tooltip(external),
        };
    }

    private static List<ConversationItem> Strip(ConversationThread? thread, string currentId, ReaderOptions options)
    {
        var items = new List<ConversationItem>();
        if (thread is null || thread.Emails.Count < 2)
        {
            return items;
        }

        foreach (var m in thread.Emails)
        {
            var sender = RowText.Sender(m);
            var time = m.ReceivedAt is { } at ? RowText.TimeLabel(at, options.Now, options.Zone, "smart", false) : string.Empty;
            items.Add(new ConversationItem(m.Id, sender, RowText.Initials(sender), RowText.TintIndex(sender), time, !m.Keywords.Seen, string.Equals(m.Id, currentId, StringComparison.Ordinal)));
        }

        return items;
    }
}
