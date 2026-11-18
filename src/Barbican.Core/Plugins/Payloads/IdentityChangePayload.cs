// Mirrors: Bulwark lib/plugin-hook-catalog.ts:246 { identityId?, change, identity? } (onBeforeIdentityChange). Owner set-data.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>An identity is about to be created ("create"), changed ("update") or deleted ("delete"); <see cref="Identity"/> carries the fields that will be sent.</summary>
public sealed record IdentityChangePayload(string? IdentityId, string Change, IdentityViewPayload? Identity);
