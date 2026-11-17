// Mirrors: Bulwark onAppReady (lib/plugin-hook-catalog.ts). Owner mailcore.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>The shell is up for an account.</summary>
public sealed record AppReadyPayload(string? AccountId);
