// Mirrors: Rust attachments.rs show :81 (attachmentImagePreviewsEnabled), mail.rs hideInlineImageAttachments; RFC 8601 §5 (ServerHost); R2-07 (pin, Sent/Drafts);
// Bulwark external-sender-chip.tsx:120-125 (the External chip and its tooltip, G-051). Owner mailcore.

namespace Barbican.Presentation.Mail;

/// <summary>The settings that shape a reader snapshot.</summary>
public sealed record ReaderOptions
{
    public bool AttachmentImagePreviews { get; init; } = true;

    public bool HideInlineImages { get; init; } = true;

    public DateTimeOffset Now { get; init; } = DateTimeOffset.UnixEpoch;

    public TimeZoneInfo Zone { get; init; } = TimeZoneInfo.Utc;

    /// <summary>The host of the account's server: only its Authentication-Results count (Fable F-07, D-9). Null: no chips, no verdict.</summary>
    public string? ServerHost { get; init; }

    /// <summary>The authserv-id pinned for the account (R2-07): once learned, only a trace block under this id counts. Null: not learned yet.</summary>
    public string? PinnedAuthServ { get; init; }

    /// <summary>Mailbox id to role of the account (as far as known): a message in Sent or Drafts gets no verdict and no chips (R2-07; the user's own mail).</summary>
    public IReadOnlyDictionary<string, string> MailboxRoles { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Tag names, colours and order: the header shows the tags of the message (Bulwark email-viewer.tsx:5422).</summary>
    public TagLookup Tags { get; init; } = TagLookup.None;

    /// <summary>The "External" sender chip is on (G-051; pref externalSenderLabel, Bulwark's externalMail.senderLabel).</summary>
    public bool ExternalSenderLabel { get; init; }

    /// <summary>The account's own domains (its identities'): a sender outside them gets the chip (ExternalSenderRules).</summary>
    public IReadOnlyList<string> InternalDomains { get; init; } = [];
}
