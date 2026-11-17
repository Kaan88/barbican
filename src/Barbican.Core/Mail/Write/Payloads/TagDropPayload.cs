// Mirrors: Bulwark lib/plugin-hook-catalog.ts lib/plugin-hook-catalog.ts:332 (payload of onTagDrop); spec/plugins/hooks.csv. Owner actions.

namespace Barbican.Core.Mail.Write.Payloads;

/// <summary>The payload of onTagDrop.</summary>
public sealed record TagDropPayload(IReadOnlyList<string> EmailIds, string Keyword);
