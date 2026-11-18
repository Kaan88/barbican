// Mirrors: Bulwark lib/plugin-hook-catalog.ts:291 { displayName } (onDisplayNameChange). Owner set-data.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>The account display name was changed.</summary>
public sealed record DisplayNameChangePayload(string DisplayName);
