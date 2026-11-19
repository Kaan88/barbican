// Mirrors: Rust mail.rs ListPrefs :40 (groupListByDate, showPreview, dateFormat, timeFormat, showAttachmentChips, density, hideInlineImageAttachments)
// and tag_color :84; Bulwark showTagChips (thread-list-item.tsx:901). Owner mailcore.

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

    /// <summary>Tag names, colours and order from the tag list (an unknown tag is grey, 0x9ca3af, named by its id).</summary>
    public TagLookup Tags { get; init; } = TagLookup.None;

    /// <summary>Rows show the tags a message carries (Bulwark showTagChips, default on).</summary>
    public bool ShowTagChips { get; init; } = true;
    public bool TintListRowsByAccount { get; init; }

    public bool ShowAccountDot { get; init; } = true;

    public bool MoreContrast { get; init; }

    public bool MultipleAccountSources { get; init; }

    /// <summary>Resolves a merged row id to its display name and the signed-in account's palette index.</summary>
    public Func<string, (string Name, int Color)?>? AccountOfRow { get; init; }

}
