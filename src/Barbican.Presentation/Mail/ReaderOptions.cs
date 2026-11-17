// Mirrors: Rust attachments.rs show :81 (attachmentImagePreviewsEnabled), mail.rs hideInlineImageAttachments; RFC 8601 §5 (ServerHost).
// Owner mailcore.

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
}
