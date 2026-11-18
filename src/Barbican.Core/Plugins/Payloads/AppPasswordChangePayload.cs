// Mirrors: Bulwark lib/plugin-hook-catalog.ts:289 { change } (onAppPasswordChange). Owner set-data.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>An app password was "created" or "removed" (never its secret).</summary>
public sealed record AppPasswordChangePayload(string Change);
