// Mirrors: Bulwark 1.21.2 components/mail/mail-app.tsx:5794-5844 (isFocusedMailLayout, isHorizontalMailLayout, shouldCollapseListPane, shouldHideViewerPane, shouldHideHorizontalViewerPane)
// and :6505-6560, :6921-6950 (which resize handle shows).

namespace Barbican.Presentation.Shell;

/// <summary>Which panes of the mail screen show and how they sit, for one layout and one state of the reading pane.</summary>
/// <param name="ListShown">The message list is on screen.</param>
/// <param name="ReaderShown">The reading pane is on screen.</param>
/// <param name="Stacked">The reading pane sits under the list (the horizontal layout) instead of beside it.</param>
/// <param name="ListFillsPane">The list takes the whole width of the mail pane (no reading pane beside it).</param>
/// <param name="HasSplitter">A resize handle sits between the list and the reading pane.</param>
public readonly record struct MailPanePlan(bool ListShown, bool ReaderShown, bool Stacked, bool ListFillsPane, bool HasSplitter);
