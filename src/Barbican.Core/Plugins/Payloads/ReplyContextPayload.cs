// Mirrors: Bulwark lib/plugin-hook-catalog.ts:83-85 (onBeforeReply, onBeforeReplyAll, onBeforeForward: ReplyContext). Owner composer-ui.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>The message a reply, reply all or forward answers, before the composer opens.</summary>
public sealed record ReplyContextPayload(string AccountKey, string EmailId);
