// Mirrors: Bulwark components/ui/dialog.tsx:181,231 (a dialog zooms in) and components/ui/context-menu.tsx:667 (a menu panel has no entrance animation).

namespace Barbican.App.Motion;

/// <summary>What is entering, so the one <see cref="PopIn"/> block can follow where Bulwark animates and where it does not.</summary>
public enum PopInKind
{
    /// <summary>A dialog, palette or sheet card (Bulwark zooms and fades it in): animates in every style.</summary>
    Dialog,

    /// <summary>A context menu or flyout panel: Bulwark has no entrance animation, so the Bulwark style shows it at once.</summary>
    Menu,
}