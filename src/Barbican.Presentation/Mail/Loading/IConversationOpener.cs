// Mirrors: Rust msgload.rs open :100 ("the list row, a key, the conversation strip"); Bulwark thread-conversation-view.tsx (a card opens its
// message in the reading pane). Spec I-222. Owner body.

namespace Barbican.Presentation.Mail.Loading;

/// <summary>Opens a message of the open conversation (the reader's strip) through the same path as a list row.</summary>
public interface IConversationOpener
{
    /// <summary>Opens the strip's message; false when it is not part of the open conversation.</summary>
    bool OpenConversationMessage(string emailId);
}
