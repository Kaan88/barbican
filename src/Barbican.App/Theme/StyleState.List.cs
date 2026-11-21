#pragma warning disable CA1822 // x:Bind binds instance members
// Mirrors: Bulwark components/email/thread-list-item.tsx:733-746 (row), :790-840 (avatar gutter, 40 px avatar, unread dot), :912-923 (status icons, w-3.5, gap-1.5),
// components/email/list-date-group-header.tsx:23-37 (h-9 header), components/email/email-hover-actions.tsx:271-274,333-371 (strip buttons p-1.5 rounded-md),
// components/email/email-list.tsx:1412-1425 (batch bar), components/ui/empty-state.tsx:29-45, app/globals.css:109-110 (density item py and gap 12).
// WP-5 list metrics. Bento and Nordic keep the values they had.

using Microsoft.UI.Text;
using Windows.UI.Text;

namespace Barbican.App.Theme;

public sealed partial class StyleState
{
    /// <summary>The sender avatar: Bulwark (a 40 px avatar, w-10) and Bento show it; Nordic has a dot instead.</summary>
    public Visibility ListAvatarVisibility => IsNordic ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>The avatar's side: Bulwark md avatar 40 px, the prototype's 38.</summary>
    public double ListAvatarSize => IsBulwark ? 40 : 38;

    /// <summary>Space between the avatar and the text: Bulwark's density gap 12.</summary>
    public double ListRowSpacing => IsBulwark ? 12 : MessageRowSpacing;

    /// <summary>Space between the sender, subject, preview lines: Bulwark mb-1 (4 px).</summary>
    public double ListLineSpacing => IsBulwark ? 4 : 1;

    /// <summary>The row's status icons (star, replied, forwarded, paperclip): Bulwark w-3.5 (14 px).</summary>
    public double StatusIconSize => IsBulwark ? 14 : 12;

    /// <summary>Gap between status icons: Bulwark gap-1.5.</summary>
    public double StatusIconSpacing => IsBulwark ? 6 : 4;

    /// <summary>A date-group header's height: Bulwark h-9, 30 in the prototypes.</summary>
    public double GroupHeaderHeight => IsBulwark ? 36 : 30;

    /// <summary>A date-group header's bottom rule (Bulwark border-b border-border).</summary>
    public Thickness GroupHeaderBorder => IsBulwark ? new Thickness(0, 0, 0, 1) : new Thickness(0);

    /// <summary>Bulwark: px-2 plus the button's ps-2; the prototypes: the list's pad.</summary>
    public Thickness GroupHeaderBoxPadding => IsBulwark ? new Thickness(16, 0, 12, 0) : GroupHeaderPadding;

    /// <summary>Bulwark text-sm; the prototypes 11.5.</summary>
    public double GroupHeaderSize => (IsBulwark ? 14 : 11.5) * TextScale;

    /// <summary>Bulwark font-medium; the prototypes bold.</summary>
    public FontWeight GroupHeaderWeight => IsBulwark ? FontWeights.Medium : FontWeights.Bold;

    /// <summary>Bulwark text-muted-foreground (Dim).</summary>
    public Brush GroupHeaderBrush => AppResources.Brush(IsBulwark ? "Dim" : "Faint");

    /// <summary>The strip over a hovered row: Bulwark's is flush with the row's end and takes the row colour; the prototypes' is a bordered card.</summary>
    public CornerRadius StripRadius => IsBulwark ? new CornerRadius(0) : new CornerRadius(10);

    /// <summary>Bulwark: no border (the strip is the row's own surface).</summary>
    public Thickness StripBorder => IsBulwark ? new Thickness(0) : new Thickness(1);

    /// <summary>Bulwark: flush with the row's end and its height (the row's 12 px padding undone); pe-3 on the strip.</summary>
    public Thickness StripMargin => IsBulwark ? new Thickness(0, -12, -12, -12) : new Thickness(0, -4, 0, 0);

    /// <summary>Bulwark: ps-0.5 pe-3 and the buttons centred in the row.</summary>
    public Thickness StripPadding => IsBulwark ? new Thickness(2, 0, 12, 0) : new Thickness(4);

    /// <summary>The strip fills the row's height in Bulwark, hugs its buttons in the prototypes.</summary>
    public VerticalAlignment StripVertical => IsBulwark ? VerticalAlignment.Stretch : VerticalAlignment.Top;

    /// <summary>Bulwark strip button: p-1.5 around a 16 px icon, 28 px; the prototypes 30 x 28.</summary>
    public double StripButtonWidth => IsBulwark ? 28 : 30;

    /// <summary>The strip button's corner (Bulwark rounded-md).</summary>
    public CornerRadius StripButtonRadius => IsBulwark ? ButtonRadius : new CornerRadius(8);

    /// <summary>The strip buttons' style: the shared hover action in Bulwark (100 ms system-brush hover), the icon button elsewhere.</summary>
    public Style StripButtonStyle => (Style)Application.Current.Resources[IsBulwark ? "Button.HoverAction" : "Button.Icon"];

    /// <summary>The selection bar's rule and the row below it (Bulwark border-b border-border); the prototypes' bar is a card.</summary>
    public Thickness SelectionBarBorder => IsBulwark ? new Thickness(0, 0, 0, 1) : new Thickness(0);

    /// <summary>Bulwark py-2 px-4; the prototypes' card padding.</summary>
    public Thickness SelectionBarPadding => IsBulwark ? new Thickness(16, 8, 16, 8) : new Thickness(10, 6, 10, 6);

    /// <summary>The bar is edge to edge in Bulwark, inset in the prototypes.</summary>
    public Thickness SelectionBarMargin => IsBulwark ? new Thickness(0) : new Thickness(12, 0, 12, 0);

    /// <summary>The bar's corner.</summary>
    public CornerRadius SelectionBarRadius => IsBulwark ? new CornerRadius(0) : new CornerRadius(10);

    /// <summary>The prototypes' bar draws a Track card; Bulwark's is the accent wash at 30 % (drawn by an overlay).</summary>
    public Brush SelectionBarBrush => IsBulwark ? new SolidColorBrush(Microsoft.UI.Colors.Transparent) : AppResources.Brush("Track");
}
