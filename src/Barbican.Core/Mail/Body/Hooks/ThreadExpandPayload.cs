// Mirrors: Bulwark onThreadExpand { threadId, emailIds, expanded } (lib/plugin-hook-catalog.ts:82; lib/plugin-thread-expand.ts:21: one
// message for a card in the reading pane). In M1 the reader's conversation strip unfolds one message when it is opened from the strip. Owner body.

namespace Barbican.Core.Mail.Body.Hooks;

/// <summary>Messages of a conversation were unfolded or folded.</summary>
public sealed record ThreadExpandPayload(string ThreadId, IReadOnlyList<string> EmailIds, bool Expanded);
