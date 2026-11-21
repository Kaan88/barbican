// Mirrors: Bulwark components/layout/focus-rescue.tsx:1-30 (the focus goes somewhere sensible when the element that had it goes away; it never moves a focus the user put somewhere else:
// only when the focus is on <body> and the element that had it is gone), WCAG 2.4.3. AX-03.

namespace Barbican.Presentation.Shell;

/// <summary>The rule of the focus rescue for the message list, apart from the window so it is tested without one.</summary>
public static class FocusRescue
{
    /// <summary>
    /// The list takes the focus back when the element that had it was in the list (a row that was deleted or moved away), the focus has fallen to the page
    /// (nothing, or the page itself, or something outside the shell) and the list is on screen. A focus the user moved elsewhere is left alone.
    /// </summary>
    /// <param name="focusWasInList">The last element that took the focus was inside the list.</param>
    /// <param name="focusIsLost">The focus is on nothing, on the page itself or outside the shell's content.</param>
    /// <param name="listShown">The list column is visible and the shell is not on the sign-in screen.</param>
    public static bool ShouldReturnToList(bool focusWasInList, bool focusIsLost, bool listShown) => focusWasInList && focusIsLost && listShown;
}
