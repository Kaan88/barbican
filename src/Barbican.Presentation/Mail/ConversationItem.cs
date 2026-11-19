// Mirrors: Rust mail.rs thread strip (thread_msgs); Bulwark components/email/email-viewer.tsx conversation list. Owner mailcore.

namespace Barbican.Presentation.Mail;

/// <summary>One message of the conversation strip above the body.</summary>
public sealed record ConversationItem(string Id, string Sender, string Initials, int TintIndex, string TimeLabel, bool Unread, bool IsCurrent)
{
    /// <summary>Older-message disclosure rendered immediately before this visible header.</summary>
    public string? OlderMessageDisclosure { get; init; }
}
