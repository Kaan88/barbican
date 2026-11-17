// Mirrors: Rust compose.rs:418 should_dock; Bulwark settings-store replyUnderMessage. Port by M3 composer-ui.

namespace Barbican.Presentation.Compose;

/// <summary>Whether a reply composer docks under the message it answers.</summary>
public static class ComposerDock
{
    /// <summary>True only when the setting is on, the composer is no new message, a message is open and that message is no draft: a new message, a reopened draft, or a reply with no message open takes the whole pane.</summary>
    public static bool ShouldDock(bool setting, bool isNew, bool messageOpen, bool isDraft) => setting && !isNew && messageOpen && !isDraft;
}
