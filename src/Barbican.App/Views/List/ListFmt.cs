// Mirrors: Bulwark components/email/thread-list-item.tsx:733-746 (selected row replaces the unread wash), :846-1010 (sender text-sm bold when unread, else font-medium text-muted-foreground;
// subject font-semibold when unread, else text-foreground/90; date text-xs, semibold foreground when unread), :153-164 (unread dot beside the avatar),
// :978-997 (replied / forwarded icons, an answered and forwarded message shows both). x:Bind functions of the message list; Bento and Nordic keep the shared Fmt rules.

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
}
