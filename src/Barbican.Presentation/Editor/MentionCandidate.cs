// Mirrors: Bulwark components/email/recipient-mention.tsx; settings recipientMentionsEnabled, mentionAddsRecipientTo (G-063, G-075). Contract from the M3 plan section 4.6; owner editor-view.

namespace Barbican.Presentation.Editor;

/// <summary>A recipient the user can mention with @ in the body.</summary>
public sealed record MentionCandidate(string Name, string Email);
