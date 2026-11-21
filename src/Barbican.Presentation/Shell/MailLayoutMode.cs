// Mirrors: Bulwark 1.21.2 stores/settings-store.ts:448 (type MailLayout = 'split' | 'focus' | 'horizontal').

namespace Barbican.Presentation.Shell;

/// <summary>How the mail screen arranges the message list and the open message.</summary>
public enum MailLayoutMode
{
    /// <summary>The list beside the reading pane (the default).</summary>
    Split,

    /// <summary>The list full width; an open message takes its place (back returns to the list).</summary>
    Focus,

    /// <summary>The reading pane under the list (the stacked layout).</summary>
    Horizontal,
}
