#pragma warning disable CA1822 // x:Bind binds instance members
// Mirrors: Bulwark components/files/files-header.tsx:111 (header h-14 box-content = 56 + 1 border, px-3), file-views.tsx:173 (column header h-9), :242-248 (list row h-10, border-b border-border/60, hover bg-muted/60, selected bg-selection),
// :414-418 (grid item h-11 rounded-lg border), :461-464 (grid tile rounded-lg border), files-sidebar.tsx:33-42 (sidebar row min-h-8, square, active bg-accent). Bento and Nordic keep their previous values. WP-14, FL-01, FL-04.

namespace Barbican.App.Theme;

public sealed partial class StyleState
{
    /// <summary>The corner of a list row: square in Bulwark (a full-width band with a hairline below), rounded in the legacy styles.</summary>
    public CornerRadius FilesRowRadius => IsBulwark ? new CornerRadius(0) : new CornerRadius(8);

    /// <summary>The corner of a grid tile: Bulwark rounded-lg (8), the legacy 10.</summary>
    public CornerRadius FilesTileRadius => new(IsBulwark ? 8 : 10);

    /// <summary>The corner of a sidebar row: square in Bulwark, the legacy 10.</summary>
    public CornerRadius FilesSidebarRowRadius => new(IsBulwark ? 0 : 10);

    /// <summary>A list row's height: Bulwark h-10 (40), scaled with the text, at least the large-target minimum.</summary>
    public double FilesRowHeight => IsBulwark ? Math.Max(40 * TextScale, MinimumTarget) : 38;

    /// <summary>A sidebar row's height: Bulwark min-h-8 (32).</summary>
    public double FilesSidebarRowHeight => IsBulwark ? Math.Max(32 * TextScale, MinimumTarget) : 32;
}
