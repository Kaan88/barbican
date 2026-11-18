// Mirrors: Bulwark lib/plugin-hook-catalog.ts:288, :290 { enabled } (onTotpChange, onEncryptionChange). Owner set-data.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>Two-step sign-in or encryption at rest was turned on or off.</summary>
public sealed record SecurityEnabledPayload(bool Enabled);
