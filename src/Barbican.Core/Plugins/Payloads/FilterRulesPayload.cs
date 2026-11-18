// Mirrors: Bulwark lib/plugin-hook-catalog.ts:255-:257 (onFiltersLoaded FilterRuleView[], onBeforeFiltersSave { rules }). Owner srv-data.

using System.Text.Json.Nodes;

namespace Barbican.Core.Plugins.Payloads;

/// <summary>The filter rules of an account as Bulwark stores them (onFiltersLoaded after a load, onBeforeFiltersSave before the script is written).</summary>
public sealed record FilterRulesPayload(string AccountId, IReadOnlyList<JsonNode> Rules);
