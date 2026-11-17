// Mirrors: Bulwark onEmailSelectionChange (lib/plugin-hook-catalog.ts:74). Owner mailcore.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>The messages that are selected now.</summary>
public sealed record SelectionChangedPayload(IReadOnlyList<string> EmailIds);
