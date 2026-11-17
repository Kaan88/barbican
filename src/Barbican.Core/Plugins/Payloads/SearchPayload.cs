// Mirrors: Bulwark onSearch (lib/plugin-hook-catalog.ts:145). Owner mailcore.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>A search about to run; a plugin may change the query (transform).</summary>
public sealed record SearchPayload(string Query);
