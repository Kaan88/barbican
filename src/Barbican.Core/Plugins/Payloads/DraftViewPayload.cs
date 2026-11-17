// Mirrors: Bulwark lib/plugin-hook-catalog.ts:93 (onDraftChange: DraftView). Owner composer-ui.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>What the user has written so far (debounced), the body as plain text.</summary>
public sealed record DraftViewPayload(string ComposerId, string To, string Cc, string Bcc, string Subject, string Body);
