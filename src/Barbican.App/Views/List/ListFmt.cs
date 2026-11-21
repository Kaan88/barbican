// Mirrors: Bulwark components/email/thread-list-item.tsx:733-746 (selected row replaces the unread wash), :846-1010 (sender text-sm bold when unread, else font-medium text-muted-foreground;
// subject font-semibold when unread, else text-foreground/90; date text-xs, semibold foreground when unread), :153-164 (unread dot beside the avatar),
// :978-997 (replied / forwarded icons, an answered and forwarded message shows both), :912-923 (pinned), components/email/list-date-group-header.tsx:23-37 (foldable date header). x:Bind functions of the message list; Bento and Nordic keep the shared Fmt rules.

using Microsoft.UI.Text;
using Windows.UI.Text;

namespace Barbican.App.Views.List;

/// <summary>Pure functions of the message row template for the Bulwark style (colours come from the live palette brushes).</summary>
public static class ListFmt
{
    /// <summary>Bulwark sender: bold when unread, medium when read; the prototypes' weights otherwise.</summary>
    public static FontWeight SenderWeight(bool unread, bool nordic, bool bulwark) =>
        bulwark ? (unread ? FontWeights.Bold : FontWeights.Medium) : Fmt.SenderWeight(unread, nordic);

    /// <summary>Bulwark sender: foreground when unread, muted-foreground when read.</summary>
    public static Brush SenderBrush(bool unread, bool bulwark) => Fmt.Palette(bulwark && !unread ? "Dim" : "Text");

    /// <summary>Bulwark subject: semibold when unread, normal when read.</summary>
    public static FontWeight SubjectWeight(bool unread, bool nordic, bool bulwark) =>
        bulwark ? (unread ? FontWeights.SemiBold : FontWeights.Normal) : Fmt.SubjectWeight(unread, nordic);

    /// <summary>A read subject is foreground at 90 % in Bulwark (text-foreground/90).</summary>
    public static double SubjectOpacity(bool unread, bool bulwark) => bulwark && !unread ? 0.9 : 1;

    /// <summary>Bulwark date: foreground semibold when unread, muted-foreground regular when read.</summary>
    public static Brush TimeBrush(bool unread, bool nordic, bool bulwark) => bulwark ? Fmt.Palette(unread ? "Text" : "Dim") : Fmt.RowTimeBrush(unread, nordic);

    /// <summary>The date's weight.</summary>
    public static FontWeight TimeWeight(bool unread, bool nordic, bool bulwark) =>
        bulwark ? (unread ? FontWeights.SemiBold : FontWeights.Normal) : Fmt.RowTimeWeight(nordic);

    /// <summary>The dot in front of the sender: the prototypes only. Bulwark's dot sits beside the avatar.</summary>
    public static Visibility InlineDot(bool unread, bool nordic, bool bulwark) => bulwark ? Visibility.Collapsed : Fmt.DotVis(unread, nordic);

    /// <summary>The unread dot beside the avatar (an 8 px circle in the unread colour).</summary>
    public static Visibility AvatarDot(bool unread, bool bulwark) => Fmt.Vis(unread && bulwark);

    /// <summary>The unread wash (accent at 30 %): an unread row that is not the open one.</summary>
    public static Visibility UnreadWash(bool unread, bool selected, bool bulwark) => Fmt.Vis(unread && !selected && bulwark);

    /// <summary>The reply icon: answered, alone or with the forward icon.</summary>
    public static Visibility Replied(bool answered) => Fmt.Vis(answered);

    /// <summary>The forward icon.</summary>
    public static Visibility ForwardedIcon(bool forwarded) => Fmt.Vis(forwarded);

    /// <summary>The paperclip: a message with attachments and no chips under it.</summary>
    public static Visibility Clip(bool hasAttachment, int chipCount) => Fmt.VisClip(hasAttachment, chipCount);

    /// <summary>The hovered row's strip background: the row's own surface (selected fill or hover), thread-list-item.tsx:1000-1003.</summary>
    public static Brush StripFill(bool selected, bool bulwark) => bulwark
        ? Fmt.Palette(selected ? "FeedbackSelected" : "FeedbackHover")
        : Fmt.Palette("Popup");

    /// <summary>The opaque surface under the strip: the list ground (the selected fill is translucent, the text under the strip must not show through).</summary>
    public static Brush StripBase(bool bulwark) => Fmt.Palette(bulwark ? "Bg" : "Popup");

    /// <summary>The selection bar's gap between the bar and the "select all in folder" banner.</summary>
    public static double BarSpacing(bool bulwark) => bulwark ? 0 : 6;

    /// <summary>The bar's count: text-sm font-medium foreground in Bulwark (email-list.tsx:1426-1428), 12.5 dim in the prototypes.</summary>
    public static double CountSize(bool bulwark) => bulwark ? 14 : 12.5;

    /// <summary>The count's weight.</summary>
    public static FontWeight CountWeight(bool bulwark) => bulwark ? FontWeights.Medium : FontWeights.Normal;

    /// <summary>The count's colour.</summary>
    public static Brush CountBrush(bool bulwark) => Fmt.Palette(bulwark ? "Text" : "Dim");

    /// <summary>The batch bar's count: the count alone in Bulwark (the buttons share its line), "N selected . M unread" in the prototypes.</summary>
    public static string CountText(string full, string countOnly, bool bulwark) => bulwark ? countOnly : full;

    /// <summary>The wash and ring of a ticked Bulwark row.</summary>
    public static Visibility CheckedWash(bool isChecked, bool bulwark) => Fmt.Vis(isChecked && bulwark);

    /// <summary>The starred icon: amber-400 fill in Bulwark (thread-list-item.tsx:980; the chart-3 amber of the palette), the prototypes' amber.</summary>
    public static Brush StarBrush(bool bulwark) => Fmt.Palette(bulwark ? "Chart3" : "AmberFg");

    /// <summary>A foldable date header in Bulwark is a button; every other header is a caption (list-date-group-header.tsx:23-37).</summary>
    public static Visibility FoldHeader(bool foldable, bool bulwark) => foldable && bulwark ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility PlainHeader(bool foldable, bool bulwark) => foldable && bulwark ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>px-2 around the fold button (its own ps-2 brings the chevron to 16); the captions keep the style's padding.</summary>
    public static Thickness HeaderBoxPadding(bool foldable, bool bulwark) => foldable && bulwark ? new Thickness(8, 0, 8, 0) : Barbican.App.Theme.StyleState.Current.GroupHeaderBoxPadding;

    /// <summary>The open group points down, a folded one right.</summary>
    public static string Chevron(bool collapsed) => collapsed ? "ChevronRight" : "ChevronDown";

    public static string FoldName(string title, bool collapsed) => (collapsed ? "Expand " : "Collapse ") + title;

    /// <summary>A hover button's style: Bulwark's delete turns destructive on hover (email-hover-actions.tsx:51-56), the others and the prototypes keep the shared look.</summary>
    public static Style StripButton(string id, bool bulwark) =>
        (Style)Application.Current.Resources[bulwark ? (string.Equals(id, "delete", StringComparison.Ordinal) ? "Button.HoverAction.Destructive" : "Button.HoverAction") : "Button.Icon"];

    /// <summary>The 32 px fade at the strip's start (email-hover-actions.tsx:343-351): a gradient from clear to the strip's base, Bulwark only.</summary>
    public static Visibility StripFadeVis(bool bulwark) => bulwark ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The fade's base layer, and the row's own colour over it.</summary>
    public static Brush StripFadeBase(bool bulwark) => Fade(StripBase(bulwark));

    public static Brush StripFadeFill(bool selected, bool bulwark) => Fade(StripFill(selected, bulwark));

    /// <summary>A gradient from transparent to the colour of <paramref name="solid"/>, left to right (the mask-image of the fade).</summary>
    public static Brush Fade(Brush solid)
    {
        if (solid is not SolidColorBrush plain)
        {
            return solid;
        }

        var clear = Windows.UI.Color.FromArgb(0, plain.Color.R, plain.Color.G, plain.Color.B);
        var brush = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(1, 0) };
        brush.GradientStops.Add(new GradientStop { Color = clear, Offset = 0 });
        brush.GradientStops.Add(new GradientStop { Color = plain.Color, Offset = 1 });
        return brush;
    }

    /// <summary>The verification code button of the prototypes (a plain button with the code).</summary>
    public static Visibility CodeLegacyVis(bool shown, bool bulwark) => shown && !bulwark ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Bulwark's verification code chip (verification-code-chip.tsx).</summary>
    public static Visibility CodeChipVis(bool shown, bool bulwark) => shown && bulwark ? Visibility.Visible : Visibility.Collapsed;

    public static string CodeTip(string? code) => string.IsNullOrEmpty(code) ? string.Empty : "Copy code " + code;

    /// <summary>Bulwark's scheduled-send chip (thread-list-item.tsx:924-931): the date's place while the message waits for its release time.</summary>
    public static Visibility ScheduledChip(bool scheduled) => Fmt.Vis(scheduled);

    /// <summary>The date itself, hidden while the scheduled chip takes its place.</summary>
    public static Visibility DateLabel(bool scheduled) => Fmt.Vis(!scheduled);
}
