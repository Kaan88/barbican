// Mirrors: Bulwark lib/plugin-hook-catalog.ts:256 (onFilterRuleChange { ruleId, change }). Owner srv-data.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>One rule changed in a save: <see cref="Change"/> is added, updated, deleted, reordered or toggled.</summary>
public sealed record FilterRuleChangePayload(string RuleId, string Change);
