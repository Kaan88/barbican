// Mirrors: Rust msgload.rs (what the reader shows once a message is ready; header, thread strip, attachments, banners are set together);
// spec B-SYNC-022 (header and body change together). Owner mailcore.

using Barbican.Core.Mail.Model;

namespace Barbican.Presentation.Mail;

/// <summary>Everything the reader shows for one message, applied in one go so nothing flickers.</summary>
public sealed record ReaderSnapshot
{
    public string EmailId { get; init; } = string.Empty;

    public ReaderHeader Header { get; init; } = ReaderHeader.None;

    public IReadOnlyList<ConversationItem> Conversation { get; init; } = [];

    public IReadOnlyList<AttachmentCard> Attachments { get; init; } = [];

    /// <summary>"3 attachments · 1.2 MB", empty without cards.</summary>
    public string AttachmentSummary { get; init; } = string.Empty;

    /// <summary>The picture cards replace the compact chips (the setting is on and an image is among the files).</summary>
    public bool ShowAttachmentCards { get; init; }

    public IReadOnlyList<AuthChip> AuthChips { get; init; } = [];

    public AuthVerdict AuthVerdict { get; init; }

    public BodyState BodyState { get; init; }

    /// <summary>The message with its body values, for the body view (null for a header-only snapshot).</summary>
    public EmailBody? Body { get; init; }

    /// <summary>Why the message could not be loaded.</summary>
    public string? Error { get; init; }

    /// <summary>The empty reader.</summary>
    public static ReaderSnapshot Empty { get; } = new();
}
