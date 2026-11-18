// Mirrors: Bulwark lib/plugin-hook-catalog.ts:195-196 (onBeforeContactDelete, onAfterContactDelete: { contactIds }). Owner con-data.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>Contacts that are about to be, or were, deleted.</summary>
public sealed record ContactDeletePayload(IReadOnlyList<string> ContactIds);
