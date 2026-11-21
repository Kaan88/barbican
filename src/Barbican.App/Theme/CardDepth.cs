// Mirrors: Tailwind shadow-lg / shadow-xl as Bulwark uses them (dialog.tsx:181, context-menu.tsx:667); see CardShadow.

namespace Barbican.App.Theme;

/// <summary>How far a floating card sits above the page (Tailwind shadow-lg, shadow-xl).</summary>
public enum CardDepth
{
    /// <summary>Menus and toasts (shadow-lg).</summary>
    Large,

    /// <summary>Dialogs and the command palette (shadow-xl).</summary>
    ExtraLarge,
}
