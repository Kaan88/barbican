// Mirrors: Rust attachments.rs show :81 (attachmentImagePreviewsEnabled), mail.rs hideInlineImageAttachments. Owner mailcore.

namespace Barbican.Presentation.Mail;

/// <summary>The settings that shape a reader snapshot.</summary>
public sealed record ReaderOptions
{
    public bool AttachmentImagePreviews { get; init; } = true;

    public bool HideInlineImages { get; init; } = true;

    public DateTimeOffset Now { get; init; } = DateTimeOffset.UnixEpoch;

    public TimeZoneInfo Zone { get; init; } = TimeZoneInfo.Utc;
}
