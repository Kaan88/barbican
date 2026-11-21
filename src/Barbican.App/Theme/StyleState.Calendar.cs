#pragma warning disable CA1822 // x:Bind binds instance members
// Mirrors: Bulwark components/calendar/calendar-app.tsx:2558-2600 (the sidebar is bg-secondary with a border at its end, 256 px; the main column is flat on bg-background), calendar-toolbar.tsx:410-490 (an h-14 row, px-3,
// a border below), event-card.tsx:343-486 (every chip and block is rounded-xs, 2 px, px-1.5 py-0.5, text-xs medium), calendar-agenda-view.tsx:233-246 (a day row: a 36 px date circle),
// calendar-app.tsx:2631 (the editor is a 400 px sheet at the end, border-s, bg-background, shadow-xl). The Bento and Nordic values stay the ones the views had. WP-12, CA-01..08.

namespace Barbican.App.Theme;

public sealed partial class StyleState
{
    /// <summary>The Calendar's main column: flat on the page ground in Bulwark, the tile in Bento and Nordic.</summary>
    public Brush CalSurfaceBrush => IsBulwark ? AppResources.Brush("Bg") : TileBrush;

    /// <summary>The Calendar's main column corners: square in Bulwark (a flat column), 26 in Bento, the pane's radius in Nordic.</summary>
    public CornerRadius CalSurfaceRadius => IsBulwark ? new CornerRadius(0) : IsNordic ? PaneRadius : new CornerRadius(26);

    /// <summary>The Calendar's main column border: none in Bulwark, a hairline otherwise.</summary>
    public Thickness CalSurfaceBorder => IsBulwark ? new Thickness(0) : new Thickness(1);

    /// <summary>The Calendar's main column padding: none in Bulwark (the toolbar and the grid run edge to edge), the tile's own otherwise.</summary>
    public Thickness CalSurfacePadding => IsBulwark ? new Thickness(0) : new Thickness(20, 16, 20, 12);

    /// <summary>The Calendar toolbar's height: h-14 (56) in Bulwark.</summary>
    public double CalToolbarHeight => IsBulwark ? 56 : 44;

    /// <summary>The Calendar toolbar's side padding: px-3 (12) in Bulwark.</summary>
    public Thickness CalToolbarPadding => IsBulwark ? new Thickness(12, 0, 12, 0) : new Thickness(0);

    /// <summary>An event chip or block's corner: rounded-xs (2) in Bulwark, <paramref name="legacy"/> otherwise.</summary>
    public double CalChipRadius(double legacy) => IsBulwark ? 2 : legacy;

    /// <summary>An icon button of the calendar rows: 32 (icon-sm) in Bulwark, <paramref name="legacy"/> otherwise.</summary>
    public double CalIconSize(double legacy) => IsBulwark ? 32 : legacy;

    /// <summary>The corner of the calendar's own buttons: Bulwark's rounded-md, <paramref name="legacy"/> otherwise.</summary>
    public CornerRadius CalButtonRadius(double legacy) => IsBulwark ? ButtonRadius : new CornerRadius(legacy);

    /// <summary>The card of a popover (the More menu, the view menu, the hover card): Bulwark's menu panel (rounded-md), the legacy popup radius otherwise.</summary>
    public CornerRadius CalPopupRadius => IsBulwark ? MenuRadius : new CornerRadius(14);

    /// <summary>The corner of a field: Bulwark rounded-md, <paramref name="legacy"/> otherwise.</summary>
    public CornerRadius CalFieldRadius(double legacy) => IsBulwark ? FieldRadius : new CornerRadius(legacy);
}
