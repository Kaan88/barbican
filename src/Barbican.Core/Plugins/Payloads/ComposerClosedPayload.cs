// Mirrors: Bulwark lib/plugin-hook-catalog.ts:92 (onComposerClose: { draftId, sent }). Owner composer-ui.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>The composer that just closed: the draft it left on the server (null when none) and whether the message was sent.</summary>
public sealed record ComposerClosedPayload(string? DraftId, bool Sent);
