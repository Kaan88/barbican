// Mirrors: M2 plan section 3.3 and 4.2 (IShellOverlay: an overlay the shell places in OverlayLayer, ordered by ZIndex). Stage F of M2; menus (menu host, dialog host, palette, shortcuts sheet) and actions (toast host) register one each.

namespace Barbican.App.Shell;

/// <summary>An overlay the shell puts in <c>OverlayLayer</c>: in-window only (ADR 0005, 0007), never a windowed popup.</summary>
public interface IShellOverlay
{
    UIElement View { get; }

    /// <summary>Higher draws above lower; the toast host sits below dialogs and menus.</summary>
    int ZIndex { get; }
}
