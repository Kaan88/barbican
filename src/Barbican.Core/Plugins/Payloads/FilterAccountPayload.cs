// Mirrors: Bulwark lib/plugin-hook-catalog.ts:258-:259 (onFiltersSave, onSieveScriptChange { accountId }). Owner srv-data.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>The account whose filter script was saved or changed.</summary>
public sealed record FilterAccountPayload(string AccountId);
