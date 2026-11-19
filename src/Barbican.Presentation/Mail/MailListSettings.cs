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

    /// <summary>Tag id to name, in the tag list's order.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> TagNames { get; init; } = [];

    /// <summary>Rows show their tags (Bulwark showTagChips).</summary>
    public bool ShowTagChips { get; init; } = true;

    /// <summary>Use account tint in merged lists.</summary>
    public bool TintListRowsByAccount { get; init; }

    /// <summary>Show an account dot in merged lists when more than one source is available.</summary>
    public bool ShowAccountDot { get; init; } = true;

    public bool MoreContrast { get; init; }

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
            TagNames = [.. tags.Select(t => KeyValuePair.Create(t.Id, t.Label))],
            ShowTagChips = prefs.Bool("showTagChips", true),
            TintListRowsByAccount = prefs.Bool("tintListRowsByAccount", false),
            ShowAccountDot = prefs.Bool("showAccountDot", true),
            MoreContrast = A11y.From(prefs, system).MoreContrast,
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
        Tags = LookupTags(),
        ShowTagChips = ShowTagChips,
        TintListRowsByAccount = TintListRowsByAccount,
        ShowAccountDot = ShowAccountDot,
        MoreContrast = MoreContrast,
    };

    /// <summary>Names, colours and order of the tags from the tag list.</summary>
    public TagLookup LookupTags()
    {
        var names = TagNames.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        var rank = TagNames.Select((kv, i) => (kv.Key, i)).ToDictionary(t => t.Key, t => t.i, StringComparer.Ordinal);
        return new TagLookup
        {
            Name = id => names.TryGetValue(id, out var n) && n.Length > 0 ? n : id,
            Color = id => TagColors.TryGetValue(id, out var c) ? c : UnknownTagColor,
            Rank = id => rank.TryGetValue(id, out var r) ? r : int.MaxValue,
        };
    }

    /// <summary>The reader options for a moment in time.</summary>
    public ReaderOptions ReaderOptionsAt(DateTimeOffset now, TimeZoneInfo zone) => new()
    {
        AttachmentImagePreviews = AttachmentImagePreviews,
        HideInlineImages = HideInlineImages,
        Now = now,
        Zone = zone,
        Tags = LookupTags(),
    };

    private static uint Tint(uint rgb, bool cvd) => cvd ? A11y.CvdColor(rgb) : rgb;
}
