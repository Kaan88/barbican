// Mirrors: Bulwark lib/plugin-hook-catalog.ts:252 ReplyIdentityContext (onSelectReplyIdentity, a transform: return the id of one of the listed identities to send from it). Owner set-data.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>The identities a reply may be sent from, the one Barbican picked (null: none) and the message being answered.</summary>
public sealed record SelectReplyIdentityPayload(IReadOnlyList<string> IdentityIds, string? IdentityId, string? SourceEmailId);
