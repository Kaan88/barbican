#pragma warning disable CA1822 // x:Bind binds instance members
// Mirrors: Bulwark components/layout/sidebar.tsx:520 (folder row: text-sm, py var(--density-sidebar-py) = 4 px around a 20 px line, so 28 px), :186-188 (row padding 8, indent 12, chevron
// slot 20), :808-813 (section header: px-3, pt-3 first / pt-5, pb-1; 24 px header buttons), :1181 (a tag row is the same row as a folder row; the tag dot is w-3 h-3, round),
// :286-345 (counts: text-xs). The Bento and Nordic values stay the ones in StyleState.cs. WP-4, SB-01, SB-03, SB-05.

using Microsoft.UI.Text;
using Windows.UI.Text;

namespace Barbican.App.Theme;

public sealed partial class StyleState
{
    /// <summary>The height of a folder, tag or shared-account row: Bulwark 28 (a 20 px line and 4 px above and below), scaled with the text, at least the large-target minimum.</summary>
    public double SbRowHeight => IsBulwark ? Math.Max(28 * TextScale, MinimumTarget) : FolderRowHeight;

    /// <summary>A row's name: Bulwark text-sm, 14 px.</summary>
    public double SbNameSize => IsBulwark ? 14 * TextScale : FolderNameSize;

    /// <summary>A row's count: Bulwark text-xs, 12 px.</summary>
    public double SbCountSize => IsBulwark ? 12 * TextScale : FolderCountSize;

    /// <summary>The disclosure chevron's corner: Bulwark rounded (4), the legacy rows 6.</summary>
    public CornerRadius SbChevronRadius => new(IsBulwark ? 4 : 6);

    /// <summary>The disclosure chevron's icon: Bulwark w-3 h-3 (12), the legacy rows 11.</summary>
    public double SbChevronIcon => IsBulwark ? 12 : 11;

    /// <summary>The corner of a row's hover and selection: square in Bulwark (a full-width band), the legacy rows' own radius.</summary>
    public CornerRadius SbRowRadius => IsBulwark ? new CornerRadius(0) : TagRowRadius;

    /// <summary>The height of a tag row: the folder row's in Bulwark, the legacy rows size to their padding.</summary>
    public double SbTagHeight => IsBulwark ? SbRowHeight : double.NaN;

    /// <summary>The padding of a tag row's content: Bulwark starts 8 px in after the 2 px selection bar.</summary>
    public Thickness SbTagPadding => IsBulwark ? new Thickness(10, 0, 8, 0) : TagRowPadding;

    /// <summary>The 2 px bar of a selected Bulwark row (the legacy rows have none).</summary>
    public Thickness SbBarThickness => IsBulwark ? new Thickness(2, 0, 0, 0) : new Thickness(0);

    /// <summary>The gap between a tag's dot and its name.</summary>
    public double SbTagSpacing => IsBulwark ? 8 : TagSpacing;

    /// <summary>The box a tag's dot sits in: Bulwark's 16 px icon slot, the legacy 10 px dot itself.</summary>
    public double SbTagDotBox => IsBulwark ? 16 : 10;

    /// <summary>The dot: Bulwark w-3 h-3 round (12), the legacy 10 px squircle.</summary>
    public double SbTagDot => IsBulwark ? 12 : 10;

    public CornerRadius SbTagDotRadius => new(IsBulwark ? 6 : 4);

    /// <summary>The disclosure chevron of a tag row: Bulwark 24 px in the 20 px slot (the -2 margin gives the extra back), the legacy 22 px at the end.</summary>
    public double SbTagChevronSize => IsBulwark ? 24 : 22;

    /// <summary>A section header's padding: Bulwark px-3 pt-5 pb-1 (the first header pt-3).</summary>
    public Thickness SbHeaderPadding => IsBulwark ? new Thickness(12, 20, 12, 4) : SectionHeaderPadding;

    public Thickness SbFirstHeaderPadding => IsBulwark ? new Thickness(12, 12, 12, 4) : FirstSectionHeaderPadding;

    /// <summary>A header's plus and gear: Bulwark h-6 w-6, rounded, -my-0.5.</summary>
    public double SbToolSize => IsBulwark ? 24 : 22;

    public CornerRadius SbToolRadius => new(IsBulwark ? 4 : 8);

    public Thickness SbToolMargin => IsBulwark ? new Thickness(0, -2, 0, -2) : new Thickness(0, -4, 0, -4);

    /// <summary>A header's hover band: Bulwark rounded-sm.</summary>
    public CornerRadius SbHeaderRadius => new(IsBulwark ? 2 : 0);

    /// <summary>The fill of a selected tag or unified row: Bulwark's accent fill, the legacy selection colour.</summary>
    public Brush SbSelectedFill => AppResources.Brush(IsBulwark ? "FeedbackSelected" : "Sel");

    public Brush SbSelectedText => AppResources.Brush(IsBulwark ? "FeedbackSelectedFg" : "SelText");

    /// <summary>A selected Bulwark row's name is semibold.</summary>
    public FontWeight SbSelectedWeight => IsBulwark ? FontWeights.SemiBold : FontWeights.Normal;

    /// <summary>The sidebar Compose pill: Bulwark h-9 rounded-full (sidebar.tsx:2728-2745).</summary>
    public CornerRadius SbComposeRadius => new(QuickTarget / 2);

    /// <summary>The icon of a header's settings button: Bulwark's gear, the legacy sliders.</summary>
    public string SbSettingsIcon => IsBulwark ? "Settings" : "Opt";

    /// <summary>The corner of the account rows under the all-inboxes row.</summary>
    public CornerRadius SbAccountRowRadius => new(IsBulwark ? 0 : 10);
}
