// Mirrors: Bulwark lib/plugin-hook-catalog.ts:242 { email, trusted } (onTrustedSenderChange). Owner set-data.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>A sender was trusted (remote content loads) or untrusted.</summary>
public sealed record TrustedSenderChangePayload(string Email, bool Trusted);
