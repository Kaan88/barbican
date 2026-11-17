// Mirrors: Rust mail.rs list_prefs :60 and unread_first_size :582, tag_color :84; Bulwark settings-store keys groupListByDate, showPreview, dateFormat,
// timeFormat, emailsPerPage, showAttachmentChips, density, hideInlineImageAttachments, interfaceLayout, mvInboxType, mvUnreadSectionSize,
// attachmentImagePreviewsEnabled, colorBlindPalette, emailKeywords. Owner mailcore.

using Barbican.Core.Mail.Rules;
using Barbican.Core.Mail.Tags;
using Barbican.Core.Settings.Accessibility;
using Barbican.Core.Storage;

namespace Barbican.Presentation.Mail;

/// <summary>The list settings, read from the prefs when a list is built.</summary>
public sealed record MailListSettings
{
    private const uint UnknownTag = 0x9ca3af;

    public bool GroupByDate { get; init; } = true;

    /// <summary>How many messages a page holds (10 to 200, default 50).</summary>
    public int PageSize { get; init; } = 50;

    /// <summary>Bulwark's Mountain View layout with the "unread first" Inbox.</summary>
    public bool UnreadFirst { get; init; }

    public int UnreadSectionSize { get; init; } = 25;

    public bool AttachmentImagePreviews { get; init; } = true;

    public bool HideInlineImages { get; init; } = true;

    public bool ShowPreview { get; init; } = true;

    public string DateFormat { get; init; } = "smart";

    public bool TwelveHour { get; init; }

    public bool AttachmentChips { get; init; } = true;

    /// <summary>Tag id to 0xRRGGBB, already in the colour-blind-safe palette when that setting is on.</summary>
    public IReadOnlyDictionary<string, uint> TagColors { get; init; } = new Dictionary<string, uint>(StringComparer.Ordinal);

    public uint UnknownTagColor { get; init; } = UnknownTag;

    /// <summary>Reads the settings (the defaults for anything not stored).</summary>
    public static MailListSettings Read(PrefsSnapshot prefs, SystemAccessibility system)
    {
        ArgumentNullException.ThrowIfNull(prefs);
        var cvd = A11y.From(prefs, system).Cvd;
        var density = prefs.Str("density", "regular");
        var tags = TagRules.Parse(prefs.Values["emailKeywords"]);
        return new MailListSettings
        {
            GroupByDate = prefs.Bool("groupListByDate", true),
            PageSize = (int)Math.Clamp(prefs.Number("emailsPerPage", 50), 10, 200),
            UnreadFirst = string.Equals(prefs.Str("interfaceLayout", "default"), "mountain-view", StringComparison.Ordinal)
                && string.Equals(prefs.Str("mvInboxType", "unread-first"), "unread-first", StringComparison.Ordinal),
            UnreadSectionSize = MailRules.UnreadSectionSize((long)prefs.Number("mvUnreadSectionSize", 25)),
            AttachmentImagePreviews = prefs.Bool("attachmentImagePreviewsEnabled", true),
            HideInlineImages = prefs.Bool("hideInlineImageAttachments", true),
            ShowPreview = prefs.Bool("showPreview", true),
            DateFormat = prefs.Str("dateFormat", "smart"),
            TwelveHour = string.Equals(prefs.Str("timeFormat", "24h"), "12h", StringComparison.Ordinal),
            AttachmentChips = prefs.Bool("showAttachmentChips", true) && density is "regular" or "comfortable",
            TagColors = tags.ToDictionary(t => t.Id, t => Tint(TagRules.ColorRgb(t.Color), cvd), StringComparer.Ordinal),
            UnknownTagColor = Tint(UnknownTag, cvd),
        };
    }

    /// <summary>The row context for a moment in time.</summary>
    public RowContext RowContextAt(DateTimeOffset now, TimeZoneInfo zone) => new()
    {
        ShowPreview = ShowPreview,
        DateFormat = DateFormat,
        TwelveHour = TwelveHour,
        AttachmentChips = AttachmentChips,
        HideInlineImages = HideInlineImages,
        Now = now,
        Zone = zone,
        TagColor = id => TagColors.TryGetValue(id, out var c) ? c : UnknownTagColor,
    };

    /// <summary>The reader options for a moment in time.</summary>
    public ReaderOptions ReaderOptionsAt(DateTimeOffset now, TimeZoneInfo zone) => new()
    {
        AttachmentImagePreviews = AttachmentImagePreviews,
        HideInlineImages = HideInlineImages,
        Now = now,
        Zone = zone,
    };

    private static uint Tint(uint rgb, bool cvd) => cvd ? A11y.CvdColor(rgb) : rgb;
}
