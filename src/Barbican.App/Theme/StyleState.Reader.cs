#pragma warning disable CA1822 // x:Bind binds instance members
// Mirrors: Bulwark components/email/reader-attachments.tsx:165 (attachment chip: h-14 rounded-lg border border-border bg-card, hover bg-muted/50).

namespace Barbican.App.Theme;

public sealed partial class StyleState
{
    /// <summary>An attachment chip's corners (Bulwark rounded-lg, 8); Bento and Nordic keep their card radius.</summary>
    public CornerRadius AttachmentRadius => IsBulwark ? new CornerRadius(8) : CardRadius;

    /// <summary>The chip's border: Bulwark draws a 1 px border-border line, Bento too, Nordic none.</summary>
    public Thickness AttachmentBorder => IsBulwark ? new Thickness(1) : CardBorder;

    /// <summary>The chip's resting fill: Bulwark bg-card (Tile); the other styles keep their raised tile (Tile2).</summary>
    public Brush AttachmentFill => (Brush)Application.Current.Resources[IsBulwark ? "Tile" : "Tile2"];

    /// <summary>The file-type badge on a chip (Bulwark rounded-md, 6; the other styles keep their 10).</summary>
    public CornerRadius AttachmentThumbRadius => IsBulwark ? new CornerRadius(6) : new CornerRadius(10);

    /// <summary>A banner under the header (Bulwark rounded-md, 6; Bento and Nordic keep their pill-like 14).</summary>
    public CornerRadius BannerRadius => IsBulwark ? new CornerRadius(6) : new CornerRadius(14);

    /// <summary>A tag badge is a full pill (Bulwark tag-badge.tsx:23 rounded-full) at the 22 px row height.</summary>
    public CornerRadius TagRadius => new(11);

    /// <summary>The quick reply's Cancel and Open full editor: ghost buttons in Bulwark (email-viewer.tsx:6640), the outlined action in the other styles.</summary>
    public Style QuickReplyGhostStyle => (Style)Application.Current.Resources[IsBulwark ? "Button.Icon" : "Button.Action"];
}
