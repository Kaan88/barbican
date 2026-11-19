// Mirrors: Rust msgload.rs (what the reader shows once a message is ready; header, thread strip, attachments, banners are set together);
// spec B-SYNC-022 (header and body change together). Owner mailcore.

using Barbican.Core.Mail.Model;

namespace Barbican.Presentation.Mail;

/// <summary>Everything the reader shows for one message, applied in one go so nothing flickers.</summary>
public sealed record ReaderSnapshot
{
    public string EmailId { get; init; } = string.Empty;

    /// <summary>The active list login captured by the loader when it opened this body.</summary>
    public string? ListAccountKey { get; init; }

    public ReaderHeader Header { get; init; } = ReaderHeader.None;

    public IReadOnlyList<ConversationItem> Conversation { get; init; } = [];

    public IReadOnlyList<AttachmentCard> Attachments { get; init; } = [];

    /// <summary>"3 attachments · 1.2 MB", empty without cards.</summary>
    public string AttachmentSummary { get; init; } = string.Empty;

    /// <summary>The picture cards replace the compact chips (the setting is on and an image is among the files).</summary>
    public bool ShowAttachmentCards { get; init; }

    public IReadOnlyList<AuthChip> AuthChips { get; init; } = [];

    public AuthVerdict AuthVerdict { get; init; }

    /// <summary>The authserv-id whose trace block gave the results (R2-07), for the loader to pin; null when none counted.</summary>
    public string? LearnedAuthServ { get; init; }

    /// <summary>The results of the server's own trace block (R2-07), for the banners (one-click needs the server's DKIM pass).</summary>
    public IReadOnlyList<Barbican.Core.Mail.AuthResults.AuthResult> ServerResults { get; init; } = [];

    /// <summary>The roles of the mailboxes the message is in (as far as known).</summary>
    public IReadOnlyList<string> MailboxRoles { get; init; } = [];

    /// <summary>The banners above the body (unsubscribe, receipt, invitation, remote content), set with the rest in the same UI turn.</summary>
    public IReadOnlyList<Barbican.Presentation.Reader.ReaderBanner> Banners { get; init; } = [];

    public BodyState BodyState { get; init; }

    /// <summary>The message with its body values, for the body view (null for a header-only snapshot).</summary>
    public EmailBody? Body { get; init; }

    /// <summary>Why the message could not be loaded.</summary>
    public string? Error { get; init; }

    /// <summary>The empty reader.</summary>
    public static ReaderSnapshot Empty { get; } = new();
}
