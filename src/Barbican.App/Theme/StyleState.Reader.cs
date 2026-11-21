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
}
