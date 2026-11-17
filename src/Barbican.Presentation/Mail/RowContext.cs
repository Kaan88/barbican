// Mirrors: Rust mail.rs ListPrefs :40 (groupListByDate, showPreview, dateFormat, timeFormat, showAttachmentChips, density, hideInlineImageAttachments)
// and tag_color :84. Owner mailcore.

namespace Barbican.Presentation.Mail;

/// <summary>What the clock and the settings say about how a row is drawn.</summary>
public sealed record RowContext
{
    public bool ShowPreview { get; init; } = true;

    /// <summary>smart, relative or full (Bulwark dateFormat).</summary>
    public string DateFormat { get; init; } = "smart";

    public bool TwelveHour { get; init; }

    public bool AttachmentChips { get; init; } = true;

    public bool HideInlineImages { get; init; } = true;

    public DateTimeOffset Now { get; init; } = DateTimeOffset.UnixEpoch;

    public TimeZoneInfo Zone { get; init; } = TimeZoneInfo.Utc;

    /// <summary>A tag's colour by tag id (an unknown tag is grey, 0x9ca3af).</summary>
    public Func<string, uint> TagColor { get; init; } = _ => 0x9ca3af;
}
