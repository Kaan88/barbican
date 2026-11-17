// Mirrors: Bulwark lib/plugin-hook-catalog.ts:90 (onBeforeCompose: ComposeOptions). Owner composer-ui.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>What a new composer is about to open with (kind: new, reply, replyAll, forward, forwardAsAttachment, draft); a handler returning false stops it.</summary>
public sealed record ComposeOptionsPayload(string AccountKey, string Kind, string To, string Cc, string Bcc, string Subject, string Body);
