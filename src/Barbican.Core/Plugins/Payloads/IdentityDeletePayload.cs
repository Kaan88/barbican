// Mirrors: Bulwark lib/plugin-hook-catalog.ts:249 { identityId } (onIdentityDelete). Owner set-data.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>An identity was deleted.</summary>
public sealed record IdentityDeletePayload(string IdentityId);
