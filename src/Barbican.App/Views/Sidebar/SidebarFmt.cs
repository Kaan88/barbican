// Mirrors: Bulwark components/layout/sidebar.tsx:286-345 (SidebarRowCounts: the unread count semibold in the foreground, a "/" and the total muted, an 8 px primary dot for the dot style),
// :503-533 (the selected row), :1181 (a tag row is a folder row). x:Bind functions of the folders column (WP-4, SB-03, SB-05); colours come from the live palette brushes.

using Microsoft.UI.Text;
using Windows.UI.Text;

namespace Barbican.App.Views.Sidebar;

/// <summary>Small pure functions the sidebar's templates bind to.</summary>
public static class SidebarFmt
{
    private const string Separator = " / ";
    private const string Dot = "●";
    private const uint GreyIcon = 0xa3a3a3;

    /// <summary>A folder's name: Bulwark names are plain (the selected row's semibold comes from the row template), the legacy rows bold an unread folder.</summary>
    public static FontWeight NameWeight(int unread, bool bulwark) => bulwark ? FontWeights.Normal : Fmt.FolderWeight(unread);

    /// <summary>The unread (or only) part of a count text: "3" of "3 / 40"; nothing for the dot style.</summary>
    public static string CountHead(string count)
    {
        if (string.Equals(count, Dot, StringComparison.Ordinal))
        {
            return string.Empty;
        }

        var cut = count.IndexOf(Separator, StringComparison.Ordinal);
        return cut < 0 ? count : count[..cut];
    }

    /// <summary>The total part of "3 / 40": "/ 40" (muted).</summary>
    public static string CountTail(string count)
    {
        var cut = count.IndexOf(Separator, StringComparison.Ordinal);
        return cut < 0 ? string.Empty : "/ " + count[(cut + Separator.Length)..];
    }

    public static Visibility VisCountTail(string count) => count.Contains(Separator, StringComparison.Ordinal) ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility VisCountDot(string count) => string.Equals(count, Dot, StringComparison.Ordinal) ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The Bulwark count is shown when the style is Bulwark and there is a count; the legacy rows draw their own single text.</summary>
    public static Visibility VisBulwarkCount(string count, bool bulwark) => bulwark && !string.IsNullOrEmpty(count) ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility VisLegacyCount(bool bulwark) => bulwark ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>An unread count is semibold in the foreground colour, a count of Drafts and the like is the muted total (sidebar.tsx:325-326).</summary>
    public static FontWeight CountWeight(int unread) => unread > 0 ? FontWeights.SemiBold : FontWeights.Normal;

    public static Brush CountBrush(int unread) => Fmt.Palette(unread > 0 ? "Text" : "Dim");

    /// <summary>A folder icon: the role colour when "colourful icons" is on, else the foreground (text-foreground/80).</summary>
    public static Brush IconBrush(uint rgb, bool bulwark) => bulwark && rgb == GreyIcon ? Fmt.Palette("Text") : Fmt.Rgb(rgb);

    /// <summary>
    /// A folder icon in the Bulwark style (sidebar.tsx:263-284): the chart token of its role (muted for Trash), the colour-blind-safe colour while that palette is on, else the text colour
    /// (text-foreground/80 is the icon's own 0.8 opacity). The prototypes keep the role colour of the row.
    /// </summary>
    public static Brush IconBrush(uint rgb, string? roleTint, uint roleCvdRgb, bool bulwark) =>
        !bulwark ? Fmt.Rgb(rgb)
        : roleTint is { Length: > 0 } ? Fmt.Palette(roleTint)
        : roleCvdRgb != 0 ? Fmt.Rgb(roleCvdRgb)
        : Fmt.Palette("Text");

    /// <summary>The selected fill behind a tag row.</summary>
    public static Visibility VisSelected(bool selected) => selected ? Visibility.Visible : Visibility.Collapsed;

    public static Brush TagText(bool selected, bool bulwark) => selected ? Fmt.Palette(bulwark ? "FeedbackSelectedFg" : "SelText") : Fmt.Palette("Body");

    public static FontWeight TagWeight(bool selected, bool bulwark) => selected && bulwark ? FontWeights.SemiBold : FontWeights.Normal;

    /// <summary>A tag's lead before its dot: the indent of its depth and, in Bulwark, the disclosure slot (20).</summary>
    public static double TagLead(int depth, bool bulwark) => bulwark ? (depth * 12) + 20 : depth * 14;

    /// <summary>Where a tag's disclosure chevron sits: in Bulwark in the slot before the dot (8 px base and the indent in), in the legacy rows at the end.</summary>
    public static Thickness TagChevronMargin(int depth, bool bulwark) => bulwark ? new Thickness(8 + (depth * 12), 0, 0, 0) : new Thickness(0, 0, 8, 0);

    public static HorizontalAlignment TagChevronAlign(bool bulwark) => bulwark ? HorizontalAlignment.Left : HorizontalAlignment.Right;

    /// <summary>The rule under the Bulwark sidebar head (sidebar.tsx:2684: border-b on the wrapper of the switcher and Compose), only when the head has something in it.</summary>
    public static Thickness HeadBorder(bool bulwark, Visibility switcher, Visibility compose) =>
        bulwark && (switcher == Visibility.Visible || compose == Visibility.Visible) ? new Thickness(0, 0, 0, 1) : new Thickness(0);

    /// <summary>Compose under the switcher is px-2 pb-2; Compose alone is the 56 px head row's button (h-9 centred: 10 px above and below).</summary>
    public static Thickness ComposeMargin(Visibility switcher) => switcher == Visibility.Visible ? new Thickness(8, 0, 8, 8) : new Thickness(8, 10, 8, 10);
}
